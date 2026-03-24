using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

public class GraphFlattener
{
    // The raw memory blocks that the C# Job System will read
    public NativeArray<EdgeStruct> nativeEdges;
    public NativeArray<float3> nativeWaypoints;

    // A quick lookup dictionary so our UI can still find the original OOP GameObject if the player clicks a car
    public Dictionary<int, Edge> edgeIndexToObject = new Dictionary<int, Edge>();

    // This is called once by the TrafficManager when the game boots up
    public void FlattenGraph(List<Edge> allEdges)
    {
        // 1. Count exactly how much memory we need to allocate
        int totalWaypoints = 0;
        foreach (Edge edge in allEdges)
        {
            totalWaypoints += edge.waypoints.Count;
        }

        // 2. Allocate the contiguous memory blocks!
        // Allocator.Persistent means this memory will exist until we explicitly destroy it when the game closes.
        nativeEdges = new NativeArray<EdgeStruct>(allEdges.Count, Allocator.Persistent);
        nativeWaypoints = new NativeArray<float3>(totalWaypoints, Allocator.Persistent);

        int currentWaypointOffset = 0;

        // 3. Translate the Object-Oriented graph into flat math
        for (int i = 0; i < allEdges.Count; i++)
        {
            Edge oopEdge = allEdges[i];
            edgeIndexToObject[i] = oopEdge;

            // Build the Data-Oriented Edge
            EdgeStruct flatEdge = new EdgeStruct
            {
                startWaypointIndex = currentWaypointOffset,
                waypointCount = oopEdge.waypoints.Count,
                speedLimit = oopEdge.properties.maxSpeed,
                isOneWay = oopEdge.properties.isOneWay,
                length = CalculateEdgeLength(oopEdge.waypoints)
            };

            nativeEdges[i] = flatEdge;

            // Dump this road's curves into the giant flat waypoint array
            for (int w = 0; w < oopEdge.waypoints.Count; w++)
            {
                nativeWaypoints[currentWaypointOffset] = oopEdge.waypoints[w];
                currentWaypointOffset++;
            }
        }

        Debug.Log($"Graph Flattened for Jobs! {nativeEdges.Length} Edges and {nativeWaypoints.Length} Waypoints loaded into Native Memory.");
    }

    // A helper method to pre-calculate road length so the Job doesn't have to do it every frame
    private float CalculateEdgeLength(List<Vector3> pts)
    {
        float length = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            length += Vector3.Distance(pts[i], pts[i + 1]);
        }
        return length;
    }

    // VERY IMPORTANT: C# Garbage Collection does not clean up NativeArrays.
    // We MUST manually dispose of this memory to prevent memory leaks.
    public void Dispose()
    {
        if (nativeEdges.IsCreated) nativeEdges.Dispose();
        if (nativeWaypoints.IsCreated) nativeWaypoints.Dispose();
    }
}