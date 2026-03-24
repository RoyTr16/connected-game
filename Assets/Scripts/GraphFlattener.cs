using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;

public class GraphFlattener
{
    public NativeArray<EdgeStruct> nativeEdges;
    public NativeArray<float3> centerlineWaypoints;
    public NativeArray<float3> intersectionWaypoints;
    public NativeArray<Connection> nativeConnections;

    public Dictionary<int, Edge> edgeIndexToObject = new Dictionary<int, Edge>();
    private Dictionary<Edge, int> objectToEdgeIndex = new Dictionary<Edge, int>();

    private const float LaneOffset2D = 1.5f;

    public void FlattenGraph(List<Edge> allEdges)
    {
        int totalCenterlineWaypoints = 0;

        for (int i = 0; i < allEdges.Count; i++)
        {
            edgeIndexToObject[i] = allEdges[i];
            objectToEdgeIndex[allEdges[i]] = i;
            totalCenterlineWaypoints += allEdges[i].waypoints.Count;
        }

        List<float3> intersectionCurveList = new List<float3>();
        int totalConnectionsNeeded = 0;
        foreach (Edge edge in allEdges)
        {
            totalConnectionsNeeded += GetTurnConnections_OOP(edge, true, out var _).Count;
            totalConnectionsNeeded += GetTurnConnections_OOP(edge, false, out var _).Count;
        }

        nativeEdges = new NativeArray<EdgeStruct>(allEdges.Count, Allocator.Persistent);
        centerlineWaypoints = new NativeArray<float3>(totalCenterlineWaypoints, Allocator.Persistent);
        nativeConnections = new NativeArray<Connection>(totalConnectionsNeeded, Allocator.Persistent);

        int currentWpOffset = 0;
        int currentConnOffset = 0;

        // 1. BAKE CENTERLINES (And calculate dynamic turn triggers!)
        for (int i = 0; i < allEdges.Count; i++)
        {
            Edge oopEdge = allEdges[i];
            float edgeLen = CalculateEdgeLength(oopEdge.waypoints);

            EdgeStruct flatEdge = new EdgeStruct
            {
                startWaypointIndex = currentWpOffset,
                waypointCount = oopEdge.waypoints.Count,
                length = edgeLen,
                speedLimit = oopEdge.properties.maxSpeed,
                isOneWay = oopEdge.properties.isOneWay,

                // --- FIX: DYNAMIC TURN TRIGGER ---
                // Turn starts at 10m, but NEVER takes up more than 40% of the road!
                turnTriggerDistance = edgeLen - math.min(10f, edgeLen * 0.4f),

                forwardConnectionStart = -1, forwardConnectionCount = 0,
                reverseConnectionStart = -1, reverseConnectionCount = 0
            };
            nativeEdges[i] = flatEdge;

            for (int w = 0; w < oopEdge.waypoints.Count; w++) centerlineWaypoints[currentWpOffset++] = oopEdge.waypoints[w];
        }

        // 2. BAKE CURVE CONNECTIONS
        for (int i = 0; i < allEdges.Count; i++)
        {
            Edge oopEdge = allEdges[i];
            EdgeStruct flatEdge = nativeEdges[i];

            List<ConnectionOOP> fwdTurns = GetTurnConnections_OOP(oopEdge, true, out var _);
            if(fwdTurns.Count > 0)
            {
                flatEdge.forwardConnectionStart = currentConnOffset;
                flatEdge.forwardConnectionCount = fwdTurns.Count;
                foreach (var turn in fwdTurns) nativeConnections[currentConnOffset++] = BakeIntersectionCurve(turn, intersectionCurveList);
            }

            List<ConnectionOOP> revTurns = GetTurnConnections_OOP(oopEdge, false, out var _);
            if(revTurns.Count > 0)
            {
                flatEdge.reverseConnectionStart = currentConnOffset;
                flatEdge.reverseConnectionCount = revTurns.Count;
                foreach (var turn in revTurns) nativeConnections[currentConnOffset++] = BakeIntersectionCurve(turn, intersectionCurveList);
            }

            nativeEdges[i] = flatEdge;
        }

        intersectionWaypoints = new NativeArray<float3>(intersectionCurveList.Count, Allocator.Persistent);
        for(int i = 0; i < intersectionCurveList.Count; i++) intersectionWaypoints[i] = intersectionCurveList[i];
    }

    private struct ConnectionOOP { public Edge currentEdge; public bool currentlyForward; public Edge nextEdge; public bool nextDriveForward; }

    private List<ConnectionOOP> GetTurnConnections_OOP(Edge currentEdge, bool currentlyForward, out List<ConnectionOOP> outTurns)
    {
        List<ConnectionOOP> turns = new List<ConnectionOOP>();
        Vertex exitVertex = currentlyForward ? currentEdge.vertexB : currentEdge.vertexA;

        foreach (Edge next in exitVertex.connectedEdges)
        {
            if (next == currentEdge) continue;
            bool nextDriveForward = (next.vertexA == exitVertex);
            if (next.properties.isOneWay)
            {
                if (!next.properties.isReversedOneWay && !nextDriveForward) continue;
                if (next.properties.isReversedOneWay && nextDriveForward) continue;
            }
            turns.Add(new ConnectionOOP { currentEdge = currentEdge, currentlyForward = currentlyForward, nextEdge = next, nextDriveForward = nextDriveForward });
        }
        outTurns = turns;
        return turns;
    }

    private Connection BakeIntersectionCurve(ConnectionOOP turn, List<float3> currentCurveList)
    {
        float3[] centerlineA = (float3[])turn.currentEdge.waypoints.Select(v => (float3)v).ToArray();
        float3[] centerlineB = (float3[])turn.nextEdge.waypoints.Select(v => (float3)v).ToArray();

        float lenA = nativeEdges[objectToEdgeIndex[turn.currentEdge]].length;
        float lenB = nativeEdges[objectToEdgeIndex[turn.nextEdge]].length;

        // --- FIX: DYNAMIC CURVE SIZES ---
        float curveDistA = math.min(10f, lenA * 0.4f);
        float curveDistB = math.min(10f, lenB * 0.4f);

        // Calculate absolute distance from the START of each edge
        float distFromStartA = turn.currentlyForward ? (lenA - curveDistA) : curveDistA;
        float distFromStartB = turn.nextDriveForward ? curveDistB : (lenB - curveDistB);

        float3 stopPoint = GetPointAtDistance_OOP(centerlineA, distFromStartA);
        float3 entryPoint = GetPointAtDistance_OOP(centerlineB, distFromStartB);

        float3 exitVertex = (float3)(turn.currentlyForward ? turn.currentEdge.vertexB.transform.position : turn.currentEdge.vertexA.transform.position);

        // Foolproof directional vectors
        float3 carDirA = math.normalize(exitVertex - stopPoint);
        if(math.lengthsq(carDirA) < 0.01f) carDirA = new float3(1,0,0); // Failsafe for 0 length

        float3 carDirB = math.normalize(entryPoint - exitVertex);
        if(math.lengthsq(carDirB) < 0.01f) carDirB = new float3(1,0,0);

        float3 rightA = math.normalize(new float3(carDirA.y, -carDirA.x, 0f));
        float3 rightB = math.normalize(new float3(carDirB.y, -carDirB.x, 0f));

        float3 stopPos = stopPoint + (rightA * LaneOffset2D);
        float3 entryPos = entryPoint + (rightB * LaneOffset2D);

        // --- FIX: SAFE CONTROL POINT ---
        // Instead of messy line intersections, we just push the center vertex outward.
        // This guarantees a perfect, natural curve hugging the inside of the lane.
        float3 safeControlPoint = exitVertex + (math.normalize(rightA + rightB) * LaneOffset2D);

        List<float3> curvePoints = GenerateBezierCurve(stopPos, safeControlPoint, entryPos, 12);

        int curveStart = currentCurveList.Count;
        foreach(var p in curvePoints) currentCurveList.Add(p);

        return new Connection {
            edgeIndex = objectToEdgeIndex[turn.nextEdge],
            driveForward = turn.nextDriveForward,
            curveWaypointStartIndex = curveStart,
            curveWaypointCount = curvePoints.Count,
            curveLength = CalculateCurveLength_OOP(curvePoints),
            dropoffDistance = curveDistB // Save where to drop the car!
        };
    }

    private List<float3> GenerateBezierCurve(float3 p0, float3 p1, float3 p2, int count)
    {
        List<float3> points = new List<float3>();
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            float3 blended = math.pow(1-t, 2) * p0 + 2 * (1-t) * t * p1 + math.pow(t, 2) * p2;
            points.Add(blended);
        }
        return points;
    }

    // --- FIX: SAFER, SIMPLER DISTANCE MATH ---
    private float3 GetPointAtDistance_OOP(float3[] centerline, float distFromStart)
    {
        if (centerline.Length < 2) return centerline.FirstOrDefault();
        float distTracked = 0f;
        for (int i = 0; i < centerline.Length - 1; i++)
        {
            float segLen = math.distance(centerline[i], centerline[i+1]);
            if (distFromStart <= distTracked + segLen)
            {
                return math.lerp(centerline[i], centerline[i+1], (distFromStart - distTracked) / segLen);
            }
            distTracked += segLen;
        }
        return centerline.Last();
    }

    private float CalculateCurveLength_OOP(List<float3> pts)
    {
        float length = 0;
        for (int i = 0; i < pts.Count - 1; i++) length += math.distance(pts[i], pts[i + 1]);
        return length;
    }

    private float CalculateEdgeLength(List<Vector3> pts)
    {
        float length = 0;
        for (int i = 0; i < pts.Count - 1; i++) length += Vector3.Distance(pts[i], pts[i + 1]);
        return length;
    }

    public void Dispose()
    {
        if (nativeEdges.IsCreated) nativeEdges.Dispose();
        if (centerlineWaypoints.IsCreated) centerlineWaypoints.Dispose();
        if (intersectionWaypoints.IsCreated) intersectionWaypoints.Dispose();
        if (nativeConnections.IsCreated) nativeConnections.Dispose();
    }
}