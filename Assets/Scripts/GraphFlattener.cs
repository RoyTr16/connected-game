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

    public Material asphaltMaterial;
    public float laneWidth = 3.0f;

    public Dictionary<int, LaneAuthoring> laneIndexToObject = new Dictionary<int, LaneAuthoring>();
    private Dictionary<LaneAuthoring, int> objectToLaneIndex = new Dictionary<LaneAuthoring, int>();
    private Dictionary<LaneAuthoring, Road> laneToRoad = new Dictionary<LaneAuthoring, Road>();
    private Transform _connectionContainer;

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
        // STEP 3: Discover lane-to-lane connections
        // =====================================================
        List<List<LaneAuthoring>> connectionTargets = new List<List<LaneAuthoring>>();
        bool[] hasIncomingConnection = new bool[allLanes.Count]; // Track for mesh trimming
        int totalConnections = 0;

        for (int i = 0; i < allLanes.Count; i++)
        {
            LaneAuthoring laneA = allLanes[i];
            Road roadA = laneToRoad[laneA];
            Vertex exitVertex = GetExitVertex(laneA, roadA);
            List<LaneAuthoring> targets = new List<LaneAuthoring>();

            long fromWayId = roadA.osmWayId;
            long viaNodeId = exitVertex.osmNodeId;
            long onlyToWayId = -1;

            foreach (OsmTurnRestriction r in turnRestrictions)
            {
                if (r.fromWayId == fromWayId && r.viaNodeId == viaNodeId && r.restrictionType.StartsWith("only_"))
                {
                    onlyToWayId = r.toWayId;
                    break;
                }
            }

            foreach (Road adjRoad in exitVertex.connectedRoads)
            {
                if (adjRoad == null) continue;

                if (adjRoad == roadA)
                {
                    if (exitVertex.connectedRoads.Count > 1) continue;
                }

                if (onlyToWayId != -1 && adjRoad.osmWayId != onlyToWayId) continue;

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

                    Vertex entryVertex = GetEntryVertex(laneB, adjRoad);
                    if (entryVertex == exitVertex)
                    {
                        targets.Add(laneB);
                        hasIncomingConnection[objectToLaneIndex[laneB]] = true; // Mark destination for trimming
                    }
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

        _connectionContainer = new GameObject("Graph_ConnectionMeshes").transform;

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

        // =====================================================
        // STEP 5: Trim Main Lanes & Generate Visuals
        // =====================================================
        for (int i = 0; i < allLanes.Count; i++)
        {
            LaneAuthoring lane = allLanes[i];
            float laneLen = CalculateLength(lane.waypoints);

            bool hasOutgoing = connectionTargets[i].Count > 0;
            bool hasIncoming = hasIncomingConnection[i];

            // PROPORTIONAL TRIM: Max 3.5m, but never more than 30% of the lane length.
            // This perfectly protects tiny roundabout segments!
            float trimStart = hasIncoming ? math.min(3.5f, laneLen * 0.3f) : 0f;
            float trimEnd = hasOutgoing ? math.min(3.5f, laneLen * 0.3f) : 0f;

            List<Vector3> meshWaypoints = TrimWaypoints(lane.waypoints, trimStart, trimEnd);

            if (meshWaypoints.Count >= 2 && asphaltMaterial != null)
            {
                MeshFilter mf = lane.gameObject.AddComponent<MeshFilter>();
                MeshRenderer mr = lane.gameObject.AddComponent<MeshRenderer>();
                mf.mesh = RoadMeshBuilder.CreateRoadMesh(meshWaypoints, laneWidth);
                mr.sharedMaterial = asphaltMaterial;
            }
        }
    }

    private Vertex GetExitVertex(LaneAuthoring lane, Road road)
    {
        Vector3 lastWp = lane.waypoints[lane.waypoints.Count - 1];
        float distToA = Vector3.Distance(lastWp, road.vertexA.transform.position);
        float distToB = Vector3.Distance(lastWp, road.vertexB.transform.position);
        return distToA < distToB ? road.vertexA : road.vertexB;
    }

    private Vertex GetEntryVertex(LaneAuthoring lane, Road road)
    {
        Vector3 firstWp = lane.waypoints[0];
        float distToA = Vector3.Distance(firstWp, road.vertexA.transform.position);
        float distToB = Vector3.Distance(firstWp, road.vertexB.transform.position);
        return distToA < distToB ? road.vertexA : road.vertexB;
    }

    private Connection BakeConnection(LaneAuthoring laneA, LaneAuthoring laneB, Vertex exitVertex, List<float3> curveList)
    {
        int idxA = objectToLaneIndex[laneA];
        int idxB = objectToLaneIndex[laneB];
        LaneStruct flatA = nativeLanes[idxA];
        LaneStruct flatB = nativeLanes[idxB];

        // 1. Calculate the proportional trims to protect tiny roundabout segments
        float exitTrim = math.min(3.5f, flatA.length * 0.3f);
        float entryTrim = math.min(3.5f, flatB.length * 0.3f);

        // 2. Fetch the exact 3D coordinates and tangents at the trim points
        GetPointAndDirectionAtDistance(laneA.waypoints, flatA.length - exitTrim, out Vector3 p0Vec, out Vector3 dirA);
        GetPointAndDirectionAtDistance(laneB.waypoints, entryTrim, out Vector3 p3Vec, out Vector3 dirB);

        // We DO NOT override the Z coordinates here, so bridges and ramps keep their 3D slopes!
        float3 p0 = (float3)p0Vec;
        float3 p3 = (float3)p3Vec;

        // 3. The Bulletproof Tangent Pull Method
        // We pull the control points exactly along the entry/exit tangents by 40% of the gap distance.
        // This physically guarantees a smooth, non-jagged Cubic curve every single time.
        float gapDist = Vector3.Distance(p0Vec, p3Vec);
        float pull = gapDist * 0.4f;

        float3 p1 = (float3)(p0Vec + dirA * pull);
        float3 p2 = (float3)(p3Vec - dirB * pull); // Subtract dirB because we pull backward from the destination!

        // 4. Generate the 15-segment Cubic Bezier curve
        List<float3> curvePoints = GenerateCubicBezierCurve(p0, p1, p2, p3, 15);

        // 5. Build the visual asphalt ribbon
        if (asphaltMaterial != null && _connectionContainer != null)
        {
            List<Vector3> meshPoints = new List<Vector3>(curvePoints.Count);
            foreach (float3 fp in curvePoints) meshPoints.Add((Vector3)fp);

            GameObject connObj = new GameObject("ConnectionMesh");
            connObj.transform.SetParent(_connectionContainer);

            // Micro-offset on the Z axis to prevent Z-fighting if multiple turns overlap
            connObj.transform.position = new Vector3(0, 0, -0.01f * (curveList.Count % 20));

            MeshFilter mf = connObj.AddComponent<MeshFilter>();
            MeshRenderer mr = connObj.AddComponent<MeshRenderer>();
            mf.mesh = RoadMeshBuilder.CreateRoadMesh(meshPoints, laneWidth);
            mr.sharedMaterial = asphaltMaterial;
        }

        // 6. Append points to the global list (useful if you ever need to draw gizmos)
        foreach (var p in curvePoints) curveList.Add(p);

        // 7. Return the flawless data to the DOTS physics engine
        return new Connection
        {
            laneIndex = idxB,
            p0 = p0,
            p1 = p1,
            p2 = p2,
            p3 = p3,
            curveLength = CalculateCurveLength(curvePoints),
            exitTrim = exitTrim,
            entryTrim = entryTrim
        };
    }

    // =====================================================
    // MATH UTILITIES
    // =====================================================

    private void GetPointAndDirectionAtDistance(List<Vector3> waypoints, float distance, out Vector3 pos, out Vector3 dir)
    {
        if (waypoints.Count < 2)
        {
            pos = waypoints.Count > 0 ? waypoints[0] : Vector3.zero;
            dir = Vector3.right;
            return;
        }

        float tracked = 0f;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 pA = waypoints[i];
            Vector3 pB = waypoints[i + 1];
            float segLen = Vector3.Distance(pA, pB);

            // NaN PROTECTION: Skip duplicate overlapping OSM nodes!
            if (segLen < 0.001f) continue;

            if (distance <= tracked + segLen + 0.001f)
            {
                float t = (distance - tracked) / segLen;
                pos = Vector3.Lerp(pA, pB, t);
                dir = (pB - pA).normalized;
                return;
            }
            tracked += segLen;
        }

        pos = waypoints[waypoints.Count - 1];
        dir = (waypoints[waypoints.Count - 1] - waypoints[waypoints.Count - 2]).normalized;
    }

    // Upgraded from Quadratic to Cubic!
    private List<float3> GenerateCubicBezierCurve(float3 p0, float3 p1, float3 p2, float3 p3, int count)
    {
        List<float3> points = new List<float3>();
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / (count - 1);
            float u = 1f - t;
            float3 blended = (u*u*u)*p0 + 3f*(u*u)*t*p1 + 3f*u*(t*t)*p2 + (t*t*t)*p3;
            points.Add(blended);
        }
        return points;
    }

    // Slides mathematically along the waypoints to cleanly slice off the ends
    private List<Vector3> TrimWaypoints(List<Vector3> waypoints, float trimStart, float trimEnd)
    {
        float totalLength = CalculateLength(waypoints);
        if (totalLength <= trimStart + trimEnd) return new List<Vector3>();

        List<Vector3> result = new List<Vector3>();
        float currentDist = 0f;

        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            Vector3 pA = waypoints[i];
            Vector3 pB = waypoints[i+1];
            float segLen = Vector3.Distance(pA, pB);

            float segStart = currentDist;
            float segEnd = currentDist + segLen;

            // Skip segments completely outside the trim bounds
            if (segEnd <= trimStart || segStart >= totalLength - trimEnd)
            {
                currentDist += segLen;
                continue;
            }

            // Crosses the start threshold? Interpolate the clean cut
            if (segStart < trimStart && segEnd > trimStart)
            {
                float t = (trimStart - segStart) / segLen;
                result.Add(Vector3.Lerp(pA, pB, t));
            }
            // Add original point if it's safely inside the bounds
            else if (segStart >= trimStart && segStart <= totalLength - trimEnd)
            {
                result.Add(pA);
            }

            // Crosses the end threshold? Interpolate the clean cut
            if (segStart < totalLength - trimEnd && segEnd > totalLength - trimEnd)
            {
                float t = ((totalLength - trimEnd) - segStart) / segLen;
                result.Add(Vector3.Lerp(pA, pB, t));
            }

            currentDist += segLen;
        }

        // Ensure final point is added if there was no end trim
        if (currentDist >= trimStart && currentDist <= totalLength - trimEnd)
            result.Add(waypoints[waypoints.Count - 1]);

        return result;
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