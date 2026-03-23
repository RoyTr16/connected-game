using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class LineManager : MonoBehaviour
{
    public static LineManager Instance { get; private set; }

    [Header("Route Visuals")]
    public Material routeMaterial;
    public float routeWidth = 6f;
    public Color previewColor = new Color(0, 1, 0, 0.5f);
    public Color confirmedColor = Color.blue;

    [Header("Tracing Sensitivity")]
    [Tooltip("How close to an intersection you need to be to lock it in")]
    public float nodeSnapDistance = 15f;
    [Tooltip("How far the mouse can drift off the road while tracing")]
    public float edgeSnapDistance = 50f; // Increased for a stronger magnetic feel!

    private bool _isRouting = false;
    private List<Vertex> _activeVertices = new List<Vertex>();
    private List<Edge> _activeEdges = new List<Edge>();

    // Variables for the partial tracing UX
    private Edge _hoveredEdge;
    private Vector3 _hoveredPoint;
    private int _hoveredSegmentIndex; // NEW: Tells us EXACTLY which piece of the curve we are on

    private LineRenderer _previewLine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        _previewLine = gameObject.AddComponent<LineRenderer>();
        _previewLine.startWidth = routeWidth;
        _previewLine.endWidth = routeWidth;
        _previewLine.numCapVertices = 4;
        _previewLine.numCornerVertices = 4;
        _previewLine.material = routeMaterial;
        _previewLine.startColor = previewColor;
        _previewLine.endColor = previewColor;
        _previewLine.sortingOrder = 3;
    }

    public void StartRoute(Vertex startNode)
    {
        _isRouting = true;
        _activeVertices.Clear();
        _activeEdges.Clear();
        _hoveredEdge = null;

        _activeVertices.Add(startNode);
        startNode.ToggleSelection();
    }

    public void FinishRoute()
    {
        if (!_isRouting) return;
        _isRouting = false;

        if (_activeVertices.Count > 0) _activeVertices[0].ToggleSelection();

        // --- NEW: THE SURGERY ---
        if (_hoveredEdge != null)
        {
            Vertex currentTip = _activeVertices.Last();

            // 1. Tell the MapGenerator to slice the road and drop a green station
            Vertex newStation = MapGenerator.Instance.SplitEdge(_hoveredEdge, _hoveredPoint);

            // 2. The old road is dead. Find which of the two NEW roads connects back to our route.
            Edge connectingEdge = newStation.connectedEdges.Find(e => e.GetOppositeVertex(newStation) == currentTip);

            if (connectingEdge != null)
            {
                _activeEdges.Add(connectingEdge);
                _activeVertices.Add(newStation);
            }
        }

        if (_activeEdges.Count > 0) SpawnPermanentRoute();

        _activeVertices.Clear();
        _activeEdges.Clear();
        _hoveredEdge = null;
        _previewLine.positionCount = 0;
    }

    public void UpdatePreviewLine(Vector3 liveMousePosition)
    {
        if (!_isRouting) return;

        Vertex currentTip = _activeVertices.Last();

        // --- 1. UNDO LOGIC ---
        if (_activeVertices.Count > 1)
        {
            Vertex previousTip = _activeVertices[_activeVertices.Count - 2];
            if (Vector3.Distance(liveMousePosition, previousTip.transform.position) < nodeSnapDistance)
            {
                _activeEdges.RemoveAt(_activeEdges.Count - 1);
                _activeVertices.RemoveAt(_activeVertices.Count - 1);
                _hoveredEdge = null;
                return;
            }
        }

        // --- 2. PURE MATHEMATICAL PROJECTION ---
        _hoveredEdge = null;
        float closestDist = edgeSnapDistance;

        foreach (Edge edge in currentTip.connectedEdges)
        {
            if (_activeEdges.Count > 0 && edge == _activeEdges.Last()) continue;

            // --- NEW: ONE-WAY TRAFFIC ENFORCEMENT ---
            if (edge.properties.isOneWay)
            {
                // Standard one-way: Cars can only drive from Vertex A to Vertex B
                if (!edge.properties.isReversedOneWay && currentTip == edge.vertexB) continue;

                // Reversed one-way (OSM "-1"): Cars can only drive from Vertex B to Vertex A
                if (edge.properties.isReversedOneWay && currentTip == edge.vertexA) continue;
            }
            // ----------------------------------------

            // Use our new math function to find the magnetic center point
            float distToMouse = GetClosestPointOnEdge(edge, currentTip, liveMousePosition, out Vector3 projectedPoint, out int segmentIndex);

            if (distToMouse < closestDist)
            {
                closestDist = distToMouse;
                _hoveredEdge = edge;
                _hoveredPoint = projectedPoint;
                _hoveredSegmentIndex = segmentIndex;
            }
        }

        // --- 3. LOCKING LOGIC ---
        if (_hoveredEdge != null)
        {
            Vertex opposite = _hoveredEdge.GetOppositeVertex(currentTip);

            if (Vector3.Distance(_hoveredPoint, opposite.transform.position) < nodeSnapDistance)
            {
                _activeEdges.Add(_hoveredEdge);
                _activeVertices.Add(opposite);
                _hoveredEdge = null;
            }
        }

        // --- 4. DRAW THE LIVE LINE ---
        DrawPreview();
    }

    // ==========================================
    // NEW MATHEMATICAL PROJECTION LOGIC
    // ==========================================

    private float GetClosestPointOnEdge(Edge edge, Vertex startVertex, Vector3 mousePos, out Vector3 closestPoint, out int segmentIndex)
    {
        List<Vector3> pts = new List<Vector3>(edge.waypoints);

        // Ensure we are calculating in the direction the player is dragging
        if (startVertex == edge.vertexB) pts.Reverse();

        float minDist = float.MaxValue;
        closestPoint = Vector3.zero;
        segmentIndex = -1;

        // Check every tiny straight segment that makes up the curved road
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];

            Vector3 projected = ProjectPointOnLineSegment(a, b, mousePos);
            float dist = Vector3.Distance(mousePos, projected);

            if (dist < minDist)
            {
                minDist = dist;
                closestPoint = projected;
                segmentIndex = i; // We now know EXACTLY what part of the curve the mouse is on
            }
        }
        return minDist;
    }

    // The core math: Drops a perpendicular line from your mouse to the street
    private Vector3 ProjectPointOnLineSegment(Vector3 lineStart, Vector3 lineEnd, Vector3 point)
    {
        Vector3 lineDirection = lineEnd - lineStart;
        float lineLength = lineDirection.magnitude;
        lineDirection.Normalize();

        Vector3 projectLine = point - lineStart;
        float dotProduct = Vector3.Dot(projectLine, lineDirection);

        // Clamp it so the projection doesn't fly past the ends of the road
        dotProduct = Mathf.Clamp(dotProduct, 0f, lineLength);

        return lineStart + lineDirection * dotProduct;
    }

    // ==========================================

    private void DrawPreview()
    {
        List<Vector3> previewPoints = BuildRouteWaypoints();

        if (_hoveredEdge != null && _activeVertices.Count > 0)
        {
            List<Vector3> pts = new List<Vector3>(_hoveredEdge.waypoints);
            if (_activeVertices.Last() == _hoveredEdge.vertexB) pts.Reverse();

            // 1. Add all the waypoints up to the segment the mouse is hovering over
            // (Skipping the 0th point to avoid duplicate intersection coordinates)
            for (int i = 1; i <= _hoveredSegmentIndex; i++)
            {
                previewPoints.Add(pts[i]);
            }

            // 2. Cap the line with the exact mathematical projection of the mouse
            previewPoints.Add(_hoveredPoint);
        }

        _previewLine.positionCount = previewPoints.Count;
        _previewLine.SetPositions(previewPoints.ToArray());
    }

    private void SpawnPermanentRoute()
    {
        GameObject routeObj = new GameObject("TransitRoute_Completed");
        routeObj.transform.SetParent(this.transform);

        LineRenderer lr = routeObj.AddComponent<LineRenderer>();
        lr.startWidth = routeWidth;
        lr.endWidth = routeWidth;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.material = routeMaterial;
        lr.startColor = confirmedColor;
        lr.endColor = confirmedColor;
        lr.sortingOrder = 1;

        List<Vector3> finalPoints = BuildRouteWaypoints();
        lr.positionCount = finalPoints.Count;
        lr.SetPositions(finalPoints.ToArray());
    }

    private List<Vector3> BuildRouteWaypoints()
    {
        List<Vector3> allPoints = new List<Vector3>();
        if (_activeVertices.Count == 0) return allPoints;

        allPoints.Add(_activeVertices[0].transform.position);

        for (int i = 0; i < _activeEdges.Count; i++)
        {
            Edge edge = _activeEdges[i];
            Vertex currentStart = _activeVertices[i];

            List<Vector3> edgePoints = new List<Vector3>(edge.waypoints);
            if (edge.vertexB == currentStart) edgePoints.Reverse();

            for (int j = 1; j < edgePoints.Count; j++)
            {
                allPoints.Add(edgePoints[j]);
            }
        }
        return allPoints;
    }
}