using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct FindLaneTailsJob : IJob
{
    [ReadOnly] public NativeArray<CarSpatialData> spatialData;
    public NativeArray<int> laneTailCarIndices;

    public void Execute()
    {
        // 1. Reset all lanes to "empty" (-1)
        for (int i = 0; i < laneTailCarIndices.Length; i++)
        {
            laneTailCarIndices[i] = -1;
        }

        // 2. Sweep the sorted array.
        // Because it's sorted by distance descending, the last car we see for a lane
        // is guaranteed to be the car with the lowest distance (the tail).
        for (int i = 0; i < spatialData.Length; i++)
        {
            int lane = spatialData[i].laneIndex;
            laneTailCarIndices[lane] = i;
        }
    }
}