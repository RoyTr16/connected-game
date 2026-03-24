using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct MapSpatialDataJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<CarData> cars;
    public NativeArray<CarSpatialData> spatialData;

    public void Execute(int i)
    {
        CarData car = cars[i];

        // Because the new Bezier math seamlessly handles the intersection
        // distance inside of distanceAlongLane, we don't need any complex state checks!
        spatialData[i] = new CarSpatialData
        {
            carIndex = i,
            laneIndex = car.currentLaneIndex,
            distanceAlongLane = car.distanceAlongLane
        };
    }
}