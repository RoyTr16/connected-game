using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

public class GraphFlattener
{
    public NativeArray<EdgeStruct> nativeEdges;
    public NativeArray<float3> nativeWaypoints;
    public NativeArray<Connection> nativeConnections; // NEW

    public Dictionary<int, Edge> edgeIndexToObject = new Dictionary<int, Edge>();
    private Dictionary<Edge, int> objectToEdgeIndex = new Dictionary<Edge, int>();

    public void FlattenGraph(List<Edge> allEdges)
    {
        int totalWaypoints = 0;

        // 1. Map Object to Index for quick lookups
        for (int i = 0; i < allEdges.Count; i++)
        {
            edgeIndexToObject[i] = allEdges[i];
            objectToEdgeIndex[allEdges[i]] = i;
            totalWaypoints += allEdges[i].waypoints.Count;
        }

        // 2. Count total possible connections across the whole city
        int totalConnections = 0;
        foreach (Edge edge in allEdges)
        {
            totalConnections += GetValidConnections(edge, true).Count;
            totalConnections += GetValidConnections(edge, false).Count;
        }

        // 3. Allocate Memory
        nativeEdges = new NativeArray<EdgeStruct>(allEdges.Count, Allocator.Persistent);
        nativeWaypoints = new NativeArray<float3>(totalWaypoints, Allocator.Persistent);
        nativeConnections = new NativeArray<Connection>(totalConnections, Allocator.Persistent);

        int currentWaypointOffset = 0;
        int currentConnectionOffset = 0;

        // 4. Translate Data
        for (int i = 0; i < allEdges.Count; i++)
        {
            Edge oopEdge = allEdges[i];

            // Bake Forward Connections (Exiting Vertex B)
            List<Connection> fwdConns = GetValidConnections(oopEdge, true);
            int fwdStart = currentConnectionOffset;
            for (int c = 0; c < fwdConns.Count; c++) nativeConnections[currentConnectionOffset++] = fwdConns[c];

            // Bake Reverse Connections (Exiting Vertex A)
            List<Connection> revConns = GetValidConnections(oopEdge, false);
            int revStart = currentConnectionOffset;
            for (int c = 0; c < revConns.Count; c++) nativeConnections[currentConnectionOffset++] = revConns[c];

            EdgeStruct flatEdge = new EdgeStruct
            {
                startWaypointIndex = currentWaypointOffset,
                waypointCount = oopEdge.waypoints.Count,
                speedLimit = oopEdge.properties.maxSpeed,
                isOneWay = oopEdge.properties.isOneWay,
                length = CalculateEdgeLength(oopEdge.waypoints),

                forwardConnectionStart = fwdStart,
                forwardConnectionCount = fwdConns.Count,
                reverseConnectionStart = revStart,
                reverseConnectionCount = revConns.Count
            };

            nativeEdges[i] = flatEdge;

            for (int w = 0; w < oopEdge.waypoints.Count; w++)
            {
                nativeWaypoints[currentWaypointOffset] = oopEdge.waypoints[w];
                currentWaypointOffset++;
            }
        }
    }

    // This logic ensures cars obey one-way signs and track their direction!
    private List<Connection> GetValidConnections(Edge currentEdge, bool drivingForward)
    {
        List<Connection> conns = new List<Connection>();
        Vertex exitVertex = drivingForward ? currentEdge.vertexB : currentEdge.vertexA;

        foreach (Edge next in exitVertex.connectedEdges)
        {
            if (next == currentEdge) continue; // No U-Turns unless it's a dead end

            bool nextDriveForward = (next.vertexA == exitVertex);

            // Traffic Law Check
            if (next.properties.isOneWay)
            {
                if (!next.properties.isReversedOneWay && !nextDriveForward) continue;
                if (next.properties.isReversedOneWay && nextDriveForward) continue;
            }

            conns.Add(new Connection { edgeIndex = objectToEdgeIndex[next], driveForward = nextDriveForward });
        }

        // If dead end, force a U-turn
        if (conns.Count == 0)
        {
            conns.Add(new Connection { edgeIndex = objectToEdgeIndex[currentEdge], driveForward = !drivingForward });
        }

        return conns;
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
        if (nativeWaypoints.IsCreated) nativeWaypoints.Dispose();
        if (nativeConnections.IsCreated) nativeConnections.Dispose(); // NEW
    }
}