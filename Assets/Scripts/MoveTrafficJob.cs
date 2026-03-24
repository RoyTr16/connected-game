using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MoveTrafficJob : IJobParallelFor
{
    public NativeArray<CarData> cars;

    [ReadOnly] public NativeArray<EdgeStruct> edges;
    [ReadOnly] public NativeArray<float3> centerlineWaypoints;
    [ReadOnly] public NativeArray<float3> intersectionWaypoints;
    [ReadOnly] public NativeArray<Connection> connections;

    public float deltaTime;

    public void Execute(int index)
    {
        CarData car = cars[index];

        // =========================================================================
        // PHASE 1: THE BRAIN (Update Distance & State Machine)
        // =========================================================================
        if (car.state == TrafficState.Driving)
        {
            EdgeStruct currentEdge = edges[car.currentEdgeIndex];
            car.distanceAlongEdge += car.currentSpeed * deltaTime;

            // Trigger the intersection turn
            if (car.distanceAlongEdge >= currentEdge.turnTriggerDistance)
            {
                int connStart = car.drivingForward ? currentEdge.forwardConnectionStart : currentEdge.reverseConnectionStart;
                int connCount = car.drivingForward ? currentEdge.forwardConnectionCount : currentEdge.reverseConnectionCount;

                if (connCount > 0)
                {
                    Unity.Mathematics.Random rand = new Unity.Mathematics.Random(car.randomSeed);
                    int randomOffset = rand.NextInt(0, connCount);
                    Connection nextTurn = connections[connStart + randomOffset];

                    // Transition State
                    car.state = TrafficState.NavigatingIntersection;
                    car.currentEdgeIndex = nextTurn.edgeIndex;
                    car.drivingForward = nextTurn.driveForward;
                    car.currentCurveWaypointStartIndex = nextTurn.curveWaypointStartIndex;
                    car.curveWaypointCount = nextTurn.curveWaypointCount;
                    car.maxSpeed = nextTurn.curveLength;
                    car.curveDropoffDistance = nextTurn.dropoffDistance;

                    // Perfectly carry over momentum without double-adding deltaTime
                    car.curveDistanceAlongPath = car.distanceAlongEdge - currentEdge.turnTriggerDistance;
                    car.randomSeed = rand.NextUInt();
                }
            }
        }
        else if (car.state == TrafficState.NavigatingIntersection)
        {
            float intersectionTurnSpeed = 8f;
            car.curveDistanceAlongPath += intersectionTurnSpeed * deltaTime;

            // Trigger the return to normal driving
            if (car.curveDistanceAlongPath >= car.maxSpeed) // maxSpeed temporarily holds curveLength
            {
                car.state = TrafficState.Driving;

                // Perfectly carry over momentum into the new street
                float overflow = car.curveDistanceAlongPath - car.maxSpeed;
                car.distanceAlongEdge = car.curveDropoffDistance + overflow;

                // Restore actual road speed limit
                car.maxSpeed = edges[car.currentEdgeIndex].speedLimit;
            }
        }

        // =========================================================================
        // PHASE 2: THE PHYSICS (Calculate exact visual position ONCE per frame)
        // =========================================================================
        if (car.state == TrafficState.Driving)
        {
            EdgeStruct currentEdge = edges[car.currentEdgeIndex];
            float effectiveDistance = car.drivingForward ? car.distanceAlongEdge : (currentEdge.length - car.distanceAlongEdge);
            float distanceTracked = 0f;
            int startWp = currentEdge.startWaypointIndex;

            for (int i = 0; i < currentEdge.waypointCount - 1; i++)
            {
                float3 wpA = centerlineWaypoints[startWp + i];
                float3 wpB = centerlineWaypoints[startWp + i + 1];
                float segmentLength = math.distance(wpA, wpB);

                // The "|| i == currentEdge.waypointCount - 2" is a safety net for floating point rounding errors
                if (effectiveDistance <= distanceTracked + segmentLength || i == currentEdge.waypointCount - 2)
                {
                    float segmentProgress = math.clamp((effectiveDistance - distanceTracked) / segmentLength, 0f, 1f);
                    float3 centerPosition = math.lerp(wpA, wpB, segmentProgress);

                    float3 edgeDirection = math.normalize(wpB - wpA);
                    float3 carDirection = car.drivingForward ? edgeDirection : -edgeDirection;
                    float3 rightVector = math.normalize(new float3(carDirection.y, -carDirection.x, 0f));

                    // EXACT SNAP - No more rubber-banding!
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

            for (int i = 0; i < car.curveWaypointCount - 1; i++)
            {
                float3 wpA = intersectionWaypoints[startWp + i];
                float3 wpB = intersectionWaypoints[startWp + i + 1];
                float segmentLength = math.distance(wpA, wpB);

                if (car.curveDistanceAlongPath <= distTracked + segmentLength || i == car.curveWaypointCount - 2)
                {
                    float progress = math.clamp((car.curveDistanceAlongPath - distTracked) / segmentLength, 0f, 1f);

                    // EXACT SNAP to the perfectly smooth Bezier curve
                    car.position = math.lerp(wpA, wpB, progress);

                    float3 carDirection = math.normalize(wpB - wpA);
                    if (math.lengthsq(carDirection) > 0.001f) // Safety against zero-vectors
                    {
                        quaternion targetRotation = quaternion.LookRotationSafe(carDirection, new float3(0, 0, -1f));
                        car.rotation = math.slerp(car.rotation, targetRotation, deltaTime * 25f);
                    }
                    break;
                }
                distTracked += segmentLength;
            }
        }

        cars[index] = car; // Save back to RAM
    }
}