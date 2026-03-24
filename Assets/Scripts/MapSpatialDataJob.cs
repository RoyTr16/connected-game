using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

[BurstCompile]
public struct MapSpatialDataJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<CarData> cars;

    // WriteOnly because we are completely overwriting it every frame
    [WriteOnly] public NativeArray<CarSpatialData> spatialData;

    public void Execute(int index)
    {
        CarData car = cars[index];

        int sortLane = car.currentLaneIndex;
        float sortDist = car.distanceAlongLane;

        // FIX: Don't let turning cars disappear!
        // Project their distance onto the new lane so cars on the new lane see them coming.
        if (car.state == TrafficState.NavigatingIntersection)
        {
            sortDist = car.curveDropoffDistance - (car.maxSpeed - car.curveDistanceAlongPath);
        }

        spatialData[index] = new CarSpatialData
        {
            carIndex = index,
            laneIndex = sortLane,
            distanceAlongLane = sortDist
        };
    }
}