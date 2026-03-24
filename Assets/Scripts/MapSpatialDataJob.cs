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

        // We only care about cars actively driving on roads.
        // Cars navigating intersection curves get a "dummy" edge so they don't crash others.
        int sortEdge = (car.state == TrafficState.Driving) ? car.currentEdgeIndex : -1;

        spatialData[index] = new CarSpatialData
        {
            carIndex = index,
            edgeIndex = sortEdge,
            distanceAlongEdge = car.distanceAlongEdge
        };
    }
}