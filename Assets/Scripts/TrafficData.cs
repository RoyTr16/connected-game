using Unity.Mathematics;

// 1. The Citizen
public struct CarData
{
    public float3 position;
    public quaternion rotation;

    // Graph Tracking
    public int currentEdgeIndex;
    public float distanceAlongEdge;

    // Physics
    public float currentSpeed;
    public float maxSpeed;

    // State
    // 0 = Driving, 1 = Yielding, 2 = Stopped
    public int state;
}

// 2. The Road Segment
public struct EdgeStruct
{
    // The "Pointer" to the curves.
    // Since Jobs can't read Lists, we store ALL waypoints from ALL roads in one giant flat array.
    // This tells the Job exactly where in that giant array this specific road's curves begin and end.
    public int startWaypointIndex;
    public int waypointCount;

    public float length;
    public float speedLimit;

    // Traffic Rules
    public bool isOneWay;
}