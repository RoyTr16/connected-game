using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct FindLaneTailsJob : IJob
{
    [WriteOnly] public NativeArray<int> laneTailCarIndices;
    [ReadOnly] public NativeArray<CarSpatialData> spatialData;

    public void Execute()
    {
        // Reset all to -1 (no tail car)
        for (int i = 0; i < laneTailCarIndices.Length; i++)
            laneTailCarIndices[i] = -1;

        // spatialData is sorted by laneIndex ASC, then distanceAlongLane DESC.
        // The LAST entry for each lane is the car furthest back (lowest distance).
        for (int i = 0; i < spatialData.Length; i++)
            laneTailCarIndices[spatialData[i].laneIndex] = i;
    }
}
