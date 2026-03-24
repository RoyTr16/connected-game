using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe; // NEW: Required for the safety bypass
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MoveTrafficJob : IJobParallelFor
{
    // NEW: We disable the safety restriction because we are accessing this array
    // via the sorted spatial pointers, rather than a direct 1:1 index mapping.
    [NativeDisableParallelForRestriction]
    public NativeArray<CarData> cars;

    [ReadOnly] public NativeArray<CarSpatialData> spatialData; // NEW: The sorted pointers

    [ReadOnly] public NativeArray<EdgeStruct> edges;
    [ReadOnly] public NativeArray<float3> centerlineWaypoints;
    [ReadOnly] public NativeArray<float3> intersectionWaypoints;
    [ReadOnly] public NativeArray<Connection> connections;

    public float deltaTime;

    public void Execute(int i)
    {
        // 1. READ FROM THE SORTED POINTER
        CarSpatialData mapData = spatialData[i];
        int carIndex = mapData.carIndex;

        // 2. GET THE ACTUAL CAR DATA
        CarData car = cars[carIndex];

        // =========================================================================
        // PHASE 1: THE BRAIN (Intelligent Driver Model & Route Pre-Planning)
        // =========================================================================
        if (car.state == TrafficState.Driving)
        {
            EdgeStruct currentEdge = edges[car.currentEdgeIndex];

            // --- 1. ROUTE PRE-PLANNING ---
            // If the car just spawned or just finished a turn, it needs to roll its next move.
            if (car.upcomingConnectionIndex == -1)
            {
                int connStart = car.drivingForward ? currentEdge.forwardConnectionStart : currentEdge.reverseConnectionStart;
                int connCount = car.drivingForward ? currentEdge.forwardConnectionCount : currentEdge.reverseConnectionCount;

                if (connCount > 0)
                {
                    Unity.Mathematics.Random rand = new Unity.Mathematics.Random(car.randomSeed);
                    car.upcomingConnectionIndex = connStart + rand.NextInt(0, connCount);
                    car.randomSeed = rand.NextUInt();
                }
            }

            // Understand the planned route
            bool goingStraight = false;
            if (car.upcomingConnectionIndex != -1)
            {
                goingStraight = connections[car.upcomingConnectionIndex].isStraight;
            }

            // Dynamic Trigger: Straight cars drive to the absolute end of the edge. Turners trigger early.
            float triggerDist = goingStraight ? currentEdge.length : currentEdge.turnTriggerDistance;

            // --- 2. THE IDM & LOOK-AHEAD BRAKING ---
            float a = 2.0f;
            float b = 1.5f;
            float T = 1.2f;
            float s0 = 2.5f;

            float v = car.currentSpeed;
            float v0 = currentEdge.speedLimit;

            // LOOK-AHEAD BRAKING: If a sharp turn is coming up, dynamically lower the desired speed limit
            if (!goingStraight)
            {
                float distToTurn = triggerDist - car.distanceAlongEdge;
                // Start braking 40 meters before the corner
                if (distToTurn > 0f && distToTurn < 40f)
                {
                    float intersectionTurnSpeed = 8f;
                    v0 = math.min(v0, math.lerp(intersectionTurnSpeed, currentEdge.speedLimit, distToTurn / 40f));
                }
            }

            // ... [Keep the exact same spatialData lookup and IDM acceleration math here] ...
            // (The leaderSpeed, distanceToLeader, hasLeader, and accel formulas stay identical)

            float leaderSpeed = v0;
            float distanceToLeader = 999f;
            bool hasLeader = false;

            if (i > 0)
            {
                CarSpatialData leaderMap = spatialData[i - 1];
                if (leaderMap.edgeIndex == car.currentEdgeIndex)
                {
                    distanceToLeader = leaderMap.distanceAlongEdge - car.distanceAlongEdge - 4.0f;
                    if (distanceToLeader < 0.1f) distanceToLeader = 0.1f;

                    CarData leaderCar = cars[leaderMap.carIndex];
                    leaderSpeed = leaderCar.currentSpeed;
                    hasLeader = true;
                }
            }

            float accel = 0f;
            if (hasLeader)
            {
                float deltaV = v - leaderSpeed;
                float s_star = s0 + (v * T) + ((v * deltaV) / (2f * math.sqrt(a * b)));
                if (s_star < s0) s_star = s0;

                accel = a * (1f - math.pow(v / v0, 4f) - math.pow(s_star / distanceToLeader, 2f));

                if (distanceToLeader < 2.0f) car.currentSpeed = math.min(car.currentSpeed, leaderSpeed);
                if (distanceToLeader < 0.1f) car.currentSpeed = leaderSpeed * 0.8f;
            }
            else
            {
                accel = a * (1f - math.pow(v / v0, 4f));
            }

            car.currentSpeed += accel * deltaTime;
            if (car.currentSpeed < 0f) car.currentSpeed = 0f;
            car.distanceAlongEdge += car.currentSpeed * deltaTime;

            // --- 3. TURN EXECUTION ---
            if (car.distanceAlongEdge >= triggerDist && car.upcomingConnectionIndex != -1)
            {
                Connection nextTurn = connections[car.upcomingConnectionIndex];

                if (goingStraight)
                {
                    // Seamlessly snap to the new street without losing speed or state
                    car.currentEdgeIndex = nextTurn.edgeIndex;
                    car.drivingForward = nextTurn.driveForward;
                    car.distanceAlongEdge = car.distanceAlongEdge - currentEdge.length; // Carry overflow

                    // Clear the planned route so they roll a new one next frame
                    car.upcomingConnectionIndex = -1;
                }
                else
                {
                    // Standard Bezier Curve Transition
                    car.state = TrafficState.NavigatingIntersection;
                    car.currentEdgeIndex = nextTurn.edgeIndex;
                    car.drivingForward = nextTurn.driveForward;
                    car.currentCurveWaypointStartIndex = nextTurn.curveWaypointStartIndex;
                    car.curveWaypointCount = nextTurn.curveWaypointCount;
                    car.maxSpeed = nextTurn.curveLength;
                    car.curveDropoffDistance = nextTurn.dropoffDistance;

                    car.curveDistanceAlongPath = car.distanceAlongEdge - currentEdge.turnTriggerDistance;

                    // Clear the planned route
                    car.upcomingConnectionIndex = -1;
                }
            }
        }
        else if (car.state == TrafficState.NavigatingIntersection)
        {
            float intersectionTurnSpeed = 8f;

            // For Phase 2, we just force cars to move smoothly through the intersection without crashing math
            car.currentSpeed = intersectionTurnSpeed;
            car.curveDistanceAlongPath += car.currentSpeed * deltaTime;

            if (car.curveDistanceAlongPath >= car.maxSpeed)
            {
                car.state = TrafficState.Driving;
                float overflow = car.curveDistanceAlongPath - car.maxSpeed;
                car.distanceAlongEdge = car.curveDropoffDistance + overflow;
                car.maxSpeed = edges[car.currentEdgeIndex].speedLimit;
            }
        }

        // =========================================================================
        // PHASE 2: THE PHYSICS (Calculate exact visual position ONCE per frame)
        // =========================================================================
        // (This entire section remains completely unchanged from your current code)
        if (car.state == TrafficState.Driving)
        {
            EdgeStruct currentEdge = edges[car.currentEdgeIndex];
            float effectiveDistance = car.drivingForward ? car.distanceAlongEdge : (currentEdge.length - car.distanceAlongEdge);
            float distanceTracked = 0f;
            int startWp = currentEdge.startWaypointIndex;

            for (int w = 0; w < currentEdge.waypointCount - 1; w++)
            {
                float3 wpA = centerlineWaypoints[startWp + w];
                float3 wpB = centerlineWaypoints[startWp + w + 1];
                float segmentLength = math.distance(wpA, wpB);

                if (effectiveDistance <= distanceTracked + segmentLength || w == currentEdge.waypointCount - 2)
                {
                    float segmentProgress = math.clamp((effectiveDistance - distanceTracked) / segmentLength, 0f, 1f);
                    float3 centerPosition = math.lerp(wpA, wpB, segmentProgress);

                    float3 edgeDirection = math.normalize(wpB - wpA);
                    float3 carDirection = car.drivingForward ? edgeDirection : -edgeDirection;
                    float3 rightVector = math.normalize(new float3(carDirection.y, -carDirection.x, 0f));

                    car.position = centerPosition + (rightVector * 1.5f);

                    quaternion targetRotation = quaternion.LookRotationSafe(carDirection, new float3(0, 0, -1f));
                    car.rotation = math.slerp(car.rotation, targetRotation, deltaTime * 15f);
                    break;
                }
                distanceTracked += segmentLength;
            }
        }
        else if (car.state == TrafficState.NavigatingIntersection)
        {
            float distTracked = 0f;
            int startWp = car.currentCurveWaypointStartIndex;

            for (int w = 0; w < car.curveWaypointCount - 1; w++)
            {
                float3 wpA = intersectionWaypoints[startWp + w];
                float3 wpB = intersectionWaypoints[startWp + w + 1];
                float segmentLength = math.distance(wpA, wpB);

                if (car.curveDistanceAlongPath <= distTracked + segmentLength || w == car.curveWaypointCount - 2)
                {
                    float progress = math.clamp((car.curveDistanceAlongPath - distTracked) / segmentLength, 0f, 1f);
                    car.position = math.lerp(wpA, wpB, progress);

                    float3 carDirection = math.normalize(wpB - wpA);
                    if (math.lengthsq(carDirection) > 0.001f)
                    {
                        quaternion targetRotation = quaternion.LookRotationSafe(carDirection, new float3(0, 0, -1f));
                        car.rotation = math.slerp(car.rotation, targetRotation, deltaTime * 25f);
                    }
                    break;
                }
                distTracked += segmentLength;
            }
        }

        // Save back to RAM using the true car index!
        cars[carIndex] = car;
    }
}