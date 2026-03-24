using Unity.Mathematics;

public struct CarData
{
    public float3 position;
    public quaternion rotation;

    public int currentEdgeIndex;
    public float distanceAlongEdge;
    public float currentSpeed;
    public float maxSpeed;
    public int state;

    // NEW: Navigation
    public bool drivingForward;
    public uint randomSeed; // Required for multithreaded randomness
}

public struct EdgeStruct
{
    public int startWaypointIndex;
    public int waypointCount;
    public float length;
    public float speedLimit;
    public bool isOneWay;

    // NEW: Pointers to the Connection Array
    public int forwardConnectionStart;
    public int forwardConnectionCount;

    public int reverseConnectionStart;
    public int reverseConnectionCount;
}

// NEW: The Pre-Baked Turn Instruction
public struct Connection
{
    public int edgeIndex;
    public bool driveForward; // Do we drive A->B or B->A on the next street?
}