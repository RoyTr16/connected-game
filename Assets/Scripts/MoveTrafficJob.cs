using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MoveTrafficJob : IJobParallelFor
{
    public NativeArray<CarData> cars;

    [ReadOnly] public NativeArray<EdgeStruct> edges;
    [ReadOnly] public NativeArray<float3> waypoints;
    [ReadOnly] public NativeArray<Connection> connections; // NEW

    public float deltaTime;

    public void Execute(int index)
    {
        CarData car = cars[index];
        EdgeStruct currentEdge = edges[car.currentEdgeIndex];

        car.distanceAlongEdge += car.currentSpeed * deltaTime;

        // --- INTERSECTION NAVIGATION ---
        if (car.distanceAlongEdge >= currentEdge.length)
        {
            int connStart = car.drivingForward ? currentEdge.forwardConnectionStart : currentEdge.reverseConnectionStart;
            int connCount = car.drivingForward ? currentEdge.forwardConnectionCount : currentEdge.reverseConnectionCount;

            if (connCount > 0)
            {
                // Pull a random number to pick a turn
                Unity.Mathematics.Random rand = new Unity.Mathematics.Random(car.randomSeed);
                int randomOffset = rand.NextInt(0, connCount);

                Connection nextTurn = connections[connStart + randomOffset];

                // Update the Car's Brain
                car.currentEdgeIndex = nextTurn.edgeIndex;
                car.drivingForward = nextTurn.driveForward;
                car.distanceAlongEdge -= currentEdge.length; // Carry over excess speed into the next road

                // Roll the random seed so they don't pick the same path next time
                car.randomSeed = rand.NextUInt();
                currentEdge = edges[car.currentEdgeIndex];
            }
        }

        // --- PHYSICAL MOVEMENT ---
        // If driving backward, we just read the waypoints in reverse mathematically
        float effectiveDistance = car.drivingForward ? car.distanceAlongEdge : (currentEdge.length - car.distanceAlongEdge);

        float distanceTracked = 0f;
        int startWp = currentEdge.startWaypointIndex;

        for (int i = 0; i < currentEdge.waypointCount - 1; i++)
        {
            float3 wpA = waypoints[startWp + i];
            float3 wpB = waypoints[startWp + i + 1];
            float segmentLength = math.distance(wpA, wpB);

            if (effectiveDistance <= distanceTracked + segmentLength)
            {
                float segmentProgress = (effectiveDistance - distanceTracked) / segmentLength;
                car.position = math.lerp(wpA, wpB, segmentProgress);

                float3 direction = math.normalize(wpB - wpA);
                if (!car.drivingForward) direction = -direction; // Flip the car 180 degrees!

                car.rotation = quaternion.LookRotationSafe(direction, math.up());
                break;
            }
            distanceTracked += segmentLength;
        }

        cars[index] = car;
    }
}