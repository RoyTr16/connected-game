using System; // Required for IComparable
using Unity.Mathematics;

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

    public int currentEdgeIndex;
    public float distanceAlongEdge;
    public float currentSpeed;
    public float maxSpeed;
    public int state;

    public bool drivingForward;
    public uint randomSeed;

    public int currentCurveWaypointStartIndex;
    public int curveWaypointCount;
    public float curveDistanceAlongPath;
    public float curveDropoffDistance; // NEW: Where exactly to drop the car on the new street
}

public struct EdgeStruct
{
    public int startWaypointIndex;
    public int waypointCount;
    public float length;
    public float speedLimit;
    public bool isOneWay;

    public float turnTriggerDistance; // NEW: The exact meter mark where THIS road's turn begins

    public int forwardConnectionStart;
    public int forwardConnectionCount;
    public int reverseConnectionStart;
    public int reverseConnectionCount;
}

public struct Connection
{
    public int edgeIndex;
    public bool driveForward;
    public int curveWaypointStartIndex;
    public int curveWaypointCount;
    public float curveLength;
    public float dropoffDistance; // NEW: Passes the dynamic dropoff point to the CarData
}

// NEW: The lightweight sorting pointer
public struct CarSpatialData : IComparable<CarSpatialData>
{
    public int carIndex;
    public int edgeIndex;
    public float distanceAlongEdge;

    // This tells the Job System exactly how to sort the array
    public int CompareTo(CarSpatialData other)
    {
        // 1. Group by Road (EdgeIndex)
        int edgeComparison = edgeIndex.CompareTo(other.edgeIndex);
        if (edgeComparison != 0) return edgeComparison;

        // 2. Sort by Distance (Descending! So the car in "first place" is index 0)
        return other.distanceAlongEdge.CompareTo(distanceAlongEdge);
    }
}