using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// [BurstCompile] tells Unity to bypass standard C# compilation and translate
// this directly into highly optimized, machine-level assembly code.
[BurstCompile]
public struct MoveTrafficJob : IJobParallelFor
{
    // Read/Write: The CPU needs to update the car's position
    public NativeArray<CarData> cars;

    // ReadOnly: The CPU just needs to look at the map data
    [ReadOnly] public NativeArray<EdgeStruct> edges;
    [ReadOnly] public NativeArray<float3> waypoints;

    public float deltaTime;

    // This method is executed once for every single car in the array
    public void Execute(int index)
    {
        CarData car = cars[index];
        EdgeStruct currentEdge = edges[car.currentEdgeIndex];

        // 1. Move the car forward mathematically
        car.distanceAlongEdge += car.currentSpeed * deltaTime;

        // PHASE 1 LOOP: If they reach the end of the road, teleport back to the start.
        // (We will replace this with intersection routing in Phase 2).
        if (car.distanceAlongEdge >= currentEdge.length)
        {
            car.distanceAlongEdge = 0f;
        }

        // 2. Find EXACTLY where on the curved road the car is located
        float distanceTracked = 0f;
        int startWp = currentEdge.startWaypointIndex;

        // Loop through the tiny straight segments that make up the curved road
        for (int i = 0; i < currentEdge.waypointCount - 1; i++)
        {
            float3 wpA = waypoints[startWp + i];
            float3 wpB = waypoints[startWp + i + 1];
            float segmentLength = math.distance(wpA, wpB);

            if (car.distanceAlongEdge <= distanceTracked + segmentLength)
            {
                // The car is on this specific segment! Interpolate its exact 3D position.
                float segmentProgress = (car.distanceAlongEdge - distanceTracked) / segmentLength;
                car.position = math.lerp(wpA, wpB, segmentProgress);

                // Calculate the rotation so the car faces down the road
                float3 direction = math.normalize(wpB - wpA);
                car.rotation = quaternion.LookRotationSafe(direction, math.up());
                break;
            }
            distanceTracked += segmentLength;
        }

        // 3. Save the math back to the main array in RAM
        cars[index] = car;
    }
}