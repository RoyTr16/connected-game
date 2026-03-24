using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MoveTrafficJob : IJobParallelFor
{
    [NativeDisableParallelForRestriction]
    public NativeArray<CarData> cars;

    [ReadOnly] public NativeArray<CarSpatialData> spatialData;

    [ReadOnly] public NativeArray<LaneStruct> lanes;
    [ReadOnly] public NativeArray<float3> laneWaypoints;
    [ReadOnly] public NativeArray<float3> intersectionWaypoints;
    [ReadOnly] public NativeArray<Connection> connections;
    [ReadOnly] public NativeArray<int> laneTailCarIndices;

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
            LaneStruct currentLane = lanes[car.currentLaneIndex];

            // --- 1. ROUTE PRE-PLANNING ---
            if (car.upcomingConnectionIndex == -1)
            {
                if (currentLane.connectionCount > 0)
                {
                    Unity.Mathematics.Random rand = new Unity.Mathematics.Random(car.randomSeed);
                    car.upcomingConnectionIndex = currentLane.connectionStart + rand.NextInt(0, currentLane.connectionCount);
                    car.randomSeed = rand.NextUInt();
                }
            }

            // Understand the planned route
            bool goingStraight = false;
            if (car.upcomingConnectionIndex != -1)
            {
                goingStraight = connections[car.upcomingConnectionIndex].isStraight;
            }

            // Dynamic Trigger: Straight cars drive to the absolute end. Turners trigger early.
            float triggerDist = goingStraight ? currentLane.length : currentLane.turnTriggerDistance;

            // --- 2. THE IDM & LOOK-AHEAD BRAKING ---
            float a = 2.0f;
            float b = 1.5f;
            float T = 1.2f;
            float s0 = 2.5f;

            float v = car.currentSpeed;
            float v0 = currentLane.speedLimit;

            // LOOK-AHEAD BRAKING: If a sharp turn is coming up, dynamically lower the desired speed limit
            if (!goingStraight)
            {
                float distToTurn = triggerDist - car.distanceAlongLane;
                if (distToTurn > 0f && distToTurn < 40f)
                {
                    float intersectionTurnSpeed = 8f;
                    v0 = math.min(v0, math.lerp(intersectionTurnSpeed, currentLane.speedLimit, distToTurn / 40f));
                }
            }

            // --- SPATIAL DATA LOOKUP & IDM ---
            float leaderSpeed = v0;
            float distanceToLeader = 999f;
            bool hasLeader = false;

            if (i > 0)
            {
                CarSpatialData leaderMap = spatialData[i - 1];
                if (leaderMap.laneIndex == car.currentLaneIndex)
                {
                    distanceToLeader = leaderMap.distanceAlongLane - car.distanceAlongLane - 4.0f;
                    if (distanceToLeader < 0.1f) distanceToLeader = 0.1f;

                    CarData leaderCar = cars[leaderMap.carIndex];
                    leaderSpeed = leaderCar.currentSpeed;
                    hasLeader = true;
                }
            }

            // --- CROSS-LANE LOOK-AHEAD: See the tail car of the upcoming lane ---
            if (car.upcomingConnectionIndex != -1)
            {
                Connection nextConn = connections[car.upcomingConnectionIndex];
                int destLaneIndex = nextConn.laneIndex;
                int tailIdx = laneTailCarIndices[destLaneIndex];

                if (tailIdx != -1)
                {
                    CarData tailCar = cars[spatialData[tailIdx].carIndex];
                    float distToLaneEnd = currentLane.length - car.distanceAlongLane;
                    float crossLaneDist = distToLaneEnd + nextConn.curveLength + tailCar.distanceAlongLane - 4.0f;
                    if (crossLaneDist < 0.1f) crossLaneDist = 0.1f;

                    if (crossLaneDist < distanceToLeader)
                    {
                        distanceToLeader = crossLaneDist;
                        leaderSpeed = tailCar.currentSpeed;
                        hasLeader = true;
                    }
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
            car.distanceAlongLane += car.currentSpeed * deltaTime;

            // --- 3. TURN EXECUTION ---
            if (car.distanceAlongLane >= triggerDist && car.upcomingConnectionIndex != -1)
            {
                Connection nextTurn = connections[car.upcomingConnectionIndex];

                if (goingStraight)
                {
                    // Seamlessly snap to the new lane without losing speed or state
                    car.currentLaneIndex = nextTurn.laneIndex;
                    car.distanceAlongLane = car.distanceAlongLane - currentLane.length;
                    car.upcomingConnectionIndex = -1;
                }
                else
                {
                    // Standard Bezier Curve Transition
                    car.state = TrafficState.NavigatingIntersection;
                    car.currentLaneIndex = nextTurn.laneIndex;
                    car.currentCurveWaypointStartIndex = nextTurn.curveWaypointStartIndex;
                    car.curveWaypointCount = nextTurn.curveWaypointCount;
                    car.maxSpeed = nextTurn.curveLength;
                    car.curveDropoffDistance = nextTurn.dropoffDistance;
                    car.curveDistanceAlongPath = car.distanceAlongLane - currentLane.turnTriggerDistance;
                    car.upcomingConnectionIndex = -1;
                }
            }
        }
        else if (car.state == TrafficState.NavigatingIntersection)
        {
            float intersectionTurnSpeed = 8f;

            car.currentSpeed = intersectionTurnSpeed;
            car.curveDistanceAlongPath += car.currentSpeed * deltaTime;

            if (car.curveDistanceAlongPath >= car.maxSpeed)
            {
                car.state = TrafficState.Driving;
                float overflow = car.curveDistanceAlongPath - car.maxSpeed;
                car.distanceAlongLane = car.curveDropoffDistance + overflow;
                car.maxSpeed = lanes[car.currentLaneIndex].speedLimit;
            }
        }

        // =========================================================================
        // PHASE 2: THE PHYSICS (Position directly on pre-offset lane waypoints)
        // =========================================================================
        if (car.state == TrafficState.Driving)
        {
            LaneStruct currentLane = lanes[car.currentLaneIndex];
            float distanceTracked = 0f;
            int startWp = currentLane.startWaypointIndex;

            for (int w = 0; w < currentLane.waypointCount - 1; w++)
            {
                float3 wpA = laneWaypoints[startWp + w];
                float3 wpB = laneWaypoints[startWp + w + 1];
                float segmentLength = math.distance(wpA, wpB);

                if (car.distanceAlongLane <= distanceTracked + segmentLength || w == currentLane.waypointCount - 2)
                {
                    float segmentProgress = math.clamp((car.distanceAlongLane - distanceTracked) / segmentLength, 0f, 1f);

                    // Position directly on the lane — no offset math needed!
                    car.position = math.lerp(wpA, wpB, segmentProgress);

                    float3 direction = math.normalize(wpB - wpA);
                    quaternion targetRotation = quaternion.LookRotationSafe(direction, new float3(0, 0, -1f));
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