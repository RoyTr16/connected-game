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

    public int currentLaneIndex; // RENAMED: We drive on lanes now, not edges!
    public float distanceAlongLane; // RENAMED
    public float currentSpeed;
    public float maxSpeed;
    public int state;

    // drivingForward is GONE! Every lane flows perfectly from start to end.

    public uint randomSeed;

    public int upcomingConnectionIndex;

    // Intersection memory
    public int currentCurveWaypointStartIndex;
    public int curveWaypointCount;
    public float curveDistanceAlongPath;
    public float curveDropoffDistance;
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
    public int laneIndex; // RENAMED: Points to the next Lane, not the Edge
    public int curveWaypointStartIndex;
    public int curveWaypointCount;
    public float curveLength;
    public float dropoffDistance;
    public bool isStraight;
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