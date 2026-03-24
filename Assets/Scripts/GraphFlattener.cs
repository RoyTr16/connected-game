using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections.Generic;

public class GraphFlattener
{
    public NativeArray<LaneStruct> nativeLanes;
    public NativeArray<float3> laneWaypoints;
    public NativeArray<float3> intersectionWaypoints;
    public NativeArray<Connection> nativeConnections;

    public Dictionary<int, LaneAuthoring> laneIndexToObject = new Dictionary<int, LaneAuthoring>();
    private Dictionary<LaneAuthoring, int> objectToLaneIndex = new Dictionary<LaneAuthoring, int>();
    private Dictionary<LaneAuthoring, Road> laneToRoad = new Dictionary<LaneAuthoring, Road>();

    public void FlattenGraph(List<Road> allRoads, List<OsmTurnRestriction> turnRestrictions)
    {
        // =====================================================
        // STEP 1: Extract all LaneAuthoring children into a flat list
        // =====================================================
        List<LaneAuthoring> allLanes = new List<LaneAuthoring>();
        Dictionary<Road, int> roadToIndex = new Dictionary<Road, int>();

        for (int r = 0; r < allRoads.Count; r++)
        {
            Road road = allRoads[r];
            roadToIndex[road] = r;

            LaneAuthoring[] children = road.GetComponentsInChildren<LaneAuthoring>();
            foreach (LaneAuthoring lane in children)
            {
                int laneIdx = allLanes.Count;
                laneIndexToObject[laneIdx] = lane;
                objectToLaneIndex[lane] = laneIdx;
                laneToRoad[lane] = road;
                allLanes.Add(lane);
            }
        }

        // =====================================================
        // STEP 2: Allocate and bake lane waypoints + LaneStructs
        // =====================================================
        int totalWaypoints = 0;
        for (int i = 0; i < allLanes.Count; i++)
            totalWaypoints += allLanes[i].waypoints.Count;

        nativeLanes = new NativeArray<LaneStruct>(allLanes.Count, Allocator.Persistent);
        laneWaypoints = new NativeArray<float3>(totalWaypoints, Allocator.Persistent);

        int wpOffset = 0;
        for (int i = 0; i < allLanes.Count; i++)
        {
            LaneAuthoring lane = allLanes[i];
            Road parentRoad = laneToRoad[lane];
            float laneLen = CalculateLength(lane.waypoints);

            LaneStruct flatLane = new LaneStruct
            {
                parentEdgeIndex = roadToIndex[parentRoad],
                startWaypointIndex = wpOffset,
                waypointCount = lane.waypoints.Count,
                length = laneLen,
                speedLimit = parentRoad.speedLimit,
                turnTriggerDistance = laneLen - math.min(10f, laneLen * 0.4f),
                connectionStart = -1,
                connectionCount = 0
            };
            nativeLanes[i] = flatLane;

            for (int w = 0; w < lane.waypoints.Count; w++)
                laneWaypoints[wpOffset++] = (float3)lane.waypoints[w];
        }

        // =====================================================
        // STEP 3: Discover lane-to-lane connections (count pass)
        // =====================================================
        // A lane exiting at Vertex V connects to any lane that enters at V.
        List<List<LaneAuthoring>> connectionTargets = new List<List<LaneAuthoring>>();
        int totalConnections = 0;

        for (int i = 0; i < allLanes.Count; i++)
        {
            LaneAuthoring laneA = allLanes[i];
            Road roadA = laneToRoad[laneA];
            Vertex exitVertex = GetExitVertex(laneA, roadA);

            List<LaneAuthoring> targets = new List<LaneAuthoring>();

            // Pre-check: does an "only_" restriction apply to this road at this vertex?
            long fromWayId = roadA.osmWayId;
            long viaNodeId = exitVertex.osmNodeId;
            long onlyToWayId = -1;

            foreach (OsmTurnRestriction r in turnRestrictions)
            {
                if (r.fromWayId == fromWayId && r.viaNodeId == viaNodeId
                    && r.restrictionType.StartsWith("only_"))
                {
                    onlyToWayId = r.toWayId;
                    break;
                }
            }

            foreach (Road adjRoad in exitVertex.connectedRoads)
            {
                if (adjRoad == null) continue;

                // --- FILTER 1: Implicit U-Turn Ban ---
                if (adjRoad == roadA)
                {
                    // Allow U-turn only at physical dead-ends
                    if (exitVertex.connectedRoads.Count > 1)
                        continue;
                }

                // --- FILTER 3: "only_" restriction (checked before "no_") ---
                if (onlyToWayId != -1 && adjRoad.osmWayId != onlyToWayId)
                    continue;

                // --- FILTER 2: "no_" restriction ---
                bool blocked = false;
                foreach (OsmTurnRestriction r in turnRestrictions)
                {
                    if (r.fromWayId == fromWayId && r.viaNodeId == viaNodeId
                        && r.toWayId == adjRoad.osmWayId
                        && r.restrictionType.StartsWith("no_"))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;

                LaneAuthoring[] adjLanes = adjRoad.GetComponentsInChildren<LaneAuthoring>();
                foreach (LaneAuthoring laneB in adjLanes)
                {
                    if (laneB == laneA) continue;

                    // LaneB must START at the same vertex that LaneA exits
                    Vertex entryVertex = GetEntryVertex(laneB, adjRoad);
                    if (entryVertex == exitVertex)
                        targets.Add(laneB);
                }
            }

            connectionTargets.Add(targets);
            totalConnections += targets.Count;
        }

        // =====================================================
        // STEP 4: Bake connections with Bezier curves
        // =====================================================
        nativeConnections = new NativeArray<Connection>(totalConnections, Allocator.Persistent);
        List<float3> intersectionCurveList = new List<float3>();
        int connOffset = 0;

        for (int i = 0; i < allLanes.Count; i++)
        {
            List<LaneAuthoring> targets = connectionTargets[i];
            if (targets.Count == 0) continue;

            LaneStruct flatLane = nativeLanes[i];
            flatLane.connectionStart = connOffset;
            flatLane.connectionCount = targets.Count;

            LaneAuthoring laneA = allLanes[i];
            Road roadA = laneToRoad[laneA];
            Vertex exitVertex = GetExitVertex(laneA, roadA);

            foreach (LaneAuthoring laneB in targets)
            {
                nativeConnections[connOffset++] = BakeConnection(laneA, laneB, exitVertex, intersectionCurveList);
            }

            nativeLanes[i] = flatLane;
        }

        intersectionWaypoints = new NativeArray<float3>(intersectionCurveList.Count, Allocator.Persistent);
        for (int i = 0; i < intersectionCurveList.Count; i++)
            intersectionWaypoints[i] = intersectionCurveList[i];
    }

    // =====================================================
    // CONNECTIVITY: Determine which Vertex a lane exits/enters
    // =====================================================

    private Vertex GetExitVertex(LaneAuthoring lane, Road road)
    {
        // The exit vertex is whichever road vertex is closest to the lane's LAST waypoint
        Vector3 lastWp = lane.waypoints[lane.waypoints.Count - 1];
        float distToA = Vector3.Distance(lastWp, road.vertexA.transform.position);
        float distToB = Vector3.Distance(lastWp, road.vertexB.transform.position);
        return distToA < distToB ? road.vertexA : road.vertexB;
    }

    private Vertex GetEntryVertex(LaneAuthoring lane, Road road)
    {
        // The entry vertex is whichever road vertex is closest to the lane's FIRST waypoint
        Vector3 firstWp = lane.waypoints[0];
        float distToA = Vector3.Distance(firstWp, road.vertexA.transform.position);
        float distToB = Vector3.Distance(firstWp, road.vertexB.transform.position);
        return distToA < distToB ? road.vertexA : road.vertexB;
    }

    // =====================================================
    // BEZIER CURVE BAKING (No more cross-products!)
    // =====================================================

    private Connection BakeConnection(LaneAuthoring laneA, LaneAuthoring laneB, Vertex exitVertex, List<float3> curveList)
    {
        int idxB = objectToLaneIndex[laneB];
        LaneStruct flatA = nativeLanes[objectToLaneIndex[laneA]];
        LaneStruct flatB = nativeLanes[idxB];

        // Dynamic curve sizes (same formula: 10m max, never more than 40% of the lane)
        float curveDistA = math.min(10f, flatA.length * 0.4f);
        float curveDistB = math.min(10f, flatB.length * 0.4f);

        // Stop point on Lane A (near its end) — waypoints are already offset!
        float3 stopPoint = GetPointAtDistance(laneA.waypoints, flatA.length - curveDistA);

        // Entry point on Lane B (near its start) — waypoints are already offset!
        float3 entryPoint = GetPointAtDistance(laneB.waypoints, curveDistB);

        // Exit direction of Lane A (last two waypoints)
        List<Vector3> wpA = laneA.waypoints;
        float3 rawDirA = (float3)wpA[wpA.Count - 1] - (float3)wpA[wpA.Count - 2];
        float3 dirA = math.lengthsq(rawDirA) > 0.0001f ? math.normalize(rawDirA) : new float3(1, 0, 0);

        // Entry direction of Lane B (first two waypoints)
        List<Vector3> wpB = laneB.waypoints;
        float3 rawDirB = (float3)wpB[1] - (float3)wpB[0];
        float3 dirB = math.lengthsq(rawDirB) > 0.0001f ? math.normalize(rawDirB) : new float3(1, 0, 0);

        // Bezier control point: the vertex sits on the centerline intersection.
        // Since lane endpoints are already offset 1.5m into their physical lanes,
        // using the vertex as the control point naturally arcs through the intersection.
        float3 controlPoint = (float3)exitVertex.transform.position;

        List<float3> curvePoints = GenerateBezierCurve(stopPoint, controlPoint, entryPoint, 12);
        int curveStart = curveList.Count;
        foreach (var p in curvePoints) curveList.Add(p);

        // Straight-away check (dot > 0.95 ≈ < 18 degrees)
        float dotAngle = math.dot(dirA, dirB);
        bool isStraight = dotAngle > 0.95f;

        return new Connection
        {
            laneIndex = idxB,
            curveWaypointStartIndex = curveStart,
            curveWaypointCount = curvePoints.Count,
            curveLength = CalculateCurveLength(curvePoints),
            dropoffDistance = curveDistB,
            isStraight = isStraight
        };
    }

    // =====================================================
    // MATH UTILITIES
    // =====================================================

    private List<float3> GenerateBezierCurve(float3 p0, float3 p1, float3 p2, int count)
    {
        List<float3> points = new List<float3>();
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            float3 blended = math.pow(1 - t, 2) * p0 + 2 * (1 - t) * t * p1 + math.pow(t, 2) * p2;
            points.Add(blended);
        }
        return points;
    }

    private float3 GetPointAtDistance(List<Vector3> waypoints, float distance)
    {
        if (waypoints.Count < 2) return (float3)waypoints[0];
        float tracked = 0f;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            float segLen = Vector3.Distance(waypoints[i], waypoints[i + 1]);
            if (distance <= tracked + segLen)
            {
                float t = (distance - tracked) / segLen;
                return math.lerp((float3)waypoints[i], (float3)waypoints[i + 1], t);
            }
            tracked += segLen;
        }
        return (float3)waypoints[waypoints.Count - 1];
    }

    private float CalculateLength(List<Vector3> pts)
    {
        float length = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
            length += Vector3.Distance(pts[i], pts[i + 1]);
        return length;
    }

    private float CalculateCurveLength(List<float3> pts)
    {
        float length = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
            length += math.distance(pts[i], pts[i + 1]);
        return length;
    }

    public void Dispose()
    {
        if (nativeLanes.IsCreated) nativeLanes.Dispose();
        if (laneWaypoints.IsCreated) laneWaypoints.Dispose();
        if (intersectionWaypoints.IsCreated) intersectionWaypoints.Dispose();
        if (nativeConnections.IsCreated) nativeConnections.Dispose();
    }
}