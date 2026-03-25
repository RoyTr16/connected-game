using Unity.Mathematics;
using System;

public static class TrafficState
{
    public const int Driving = 0;
    public const int Yielding = 1;
    public const int Stopped = 2;
    public const int NavigatingIntersection = 3;
}

public struct CarData
{
    public float3 position;
    public quaternion rotation;

    public int currentLaneIndex;
    public float distanceAlongLane;

    public float currentSpeed;
    public float maxSpeed;
    public int state;

    public uint randomSeed;

    public int upcomingConnectionIndex;
}
// RENAMED: This replaces EdgeStruct
public struct LaneStruct
{
    public int parentEdgeIndex; // Keep a reference to the OSM Edge for debugging/UI
    public int startWaypointIndex;
    public int waypointCount;
    public float length;
    public float speedLimit;
    public float turnTriggerDistance;

    // We no longer need 'reverse' connections. A lane only flows one way!
    public int connectionStart;
    public int connectionCount;
}

public struct Connection
{
    public int laneIndex;
    public float3 p0;
    public float3 p1;
    public float3 p2;
    public float3 p3; // The new Cubic control point!
    public float curveLength;
    public float exitTrim;
    public float entryTrim;
}

public struct CarSpatialData : IComparable<CarSpatialData>
{
    public int carIndex;
    public int laneIndex; // RENAMED
    public float distanceAlongLane; // RENAMED

    public int CompareTo(CarSpatialData other)
    {
        int laneComparison = laneIndex.CompareTo(other.laneIndex);
        if (laneComparison != 0) return laneComparison;
        return other.distanceAlongLane.CompareTo(distanceAlongLane);
    }
}