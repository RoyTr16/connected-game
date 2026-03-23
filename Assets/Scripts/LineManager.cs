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
    public float nodeSnapDistance = 15f;
    public float edgeSnapDistance = 50f;

    private List<Vertex> _activeVertices = new List<Vertex>();
    private List<Edge> _activeEdges = new List<Edge>();

    private Vertex _lastAStarTarget = null;
    private List<Vertex> _previewVertices = new List<Vertex>();
    private List<Edge> _previewEdges = new List<Edge>();

    private Edge _hoveredEdge;
    private Vector3 _hoveredPoint;
    private int _hoveredSegmentIndex;

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
        _activeVertices.Clear();
        _activeEdges.Clear();
        ClearPreviewData();

        _activeVertices.Add(startNode);
        startNode.ToggleSelection();
    }

    // ==========================================
    // EXPLICIT UNDO LOGIC
    // ==========================================

    public void ClearPreview()
    {
        ClearPreviewData();
        _previewLine.positionCount = 0;
    }

    public bool UndoLastWaypoint()
    {
        // If we only have the starting anchor left, undoing cancels the whole route
        if (_activeVertices.Count <= 1)
        {
            if (_activeVertices.Count > 0 && _activeVertices[0].isSelected)
            {
                _activeVertices[0].ToggleSelection();
            }
            _activeVertices.Clear();
            _activeEdges.Clear();
            ClearPreview();
            return false;
        }

        // Otherwise, surgically remove the last edge and vertex from the route
        Vertex removedVertex = _activeVertices.Last();
        _activeVertices.RemoveAt(_activeVertices.Count - 1);
        _activeEdges.RemoveAt(_activeEdges.Count - 1);

        if (removedVertex.isSelected) removedVertex.ToggleSelection();

        ClearPreview();
        return true;
    }

    // ==========================================
    // STATE: A-STAR GHOST LINE
    // ==========================================

    public void UpdateAStarPreview(Vector3 liveMousePosition)
    {
        ClearPreviewData();
        Vertex currentTip = _activeVertices.Last();

        Vertex targetVertex = null;
        Collider2D[] hits = Physics2D.OverlapCircleAll(liveMousePosition, 25f);
        foreach (Collider2D hit in hits)
        {
            Vertex node = hit.GetComponent<Vertex>();
            if (node != null && !node.isSelected)
            {
                targetVertex = node;
                break;
            }
        }

        if (targetVertex != null)
        {
            List<Vertex> path = Pathfinder.FindPath(currentTip, targetVertex);
            if (path != null && path.Count > 1)
            {
                _lastAStarTarget = targetVertex;
                _previewVertices = path;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    _previewEdges.Add(GetConnectingEdge(path[i], path[i + 1]));
                }
            }
        }
        DrawPreviewLine();
    }

    public void CommitAStarWaypoint()
    {
        if (_previewEdges.Count > 0)
        {
            _activeVertices.AddRange(_previewVertices.Skip(1));
            _activeEdges.AddRange(_previewEdges);

            if (_lastAStarTarget != null && !_lastAStarTarget.isSelected)
            {
                _lastAStarTarget.ToggleSelection();
            }
            ClearPreviewData();
        }
    }

    // ==========================================
    // STATE: MAGNETIC TRACE & EDGE SPLITTING
    // ==========================================

    public void UpdateDragPreview(Vector3 liveMousePosition)
    {
        ClearPreviewData();
        Vertex currentTip = _activeVertices.Last();
        float closestDist = edgeSnapDistance;

        foreach (Edge edge in currentTip.connectedEdges)
        {
            if (_activeEdges.Count > 0 && edge == _activeEdges.Last()) continue;

            // One-Way Traffic Enforcement
            if (edge.properties.isOneWay)
            {
                if (!edge.properties.isReversedOneWay && currentTip == edge.vertexB) continue;
                if (edge.properties.isReversedOneWay && currentTip == edge.vertexA) continue;
            }

            float distToMouse = GetClosestPointOnEdge(edge, currentTip, liveMousePosition, out Vector3 projectedPoint, out int segmentIndex);

            if (distToMouse < closestDist)
            {
                closestDist = distToMouse;
                _hoveredEdge = edge;
                _hoveredPoint = projectedPoint;
                _hoveredSegmentIndex = segmentIndex;
            }
        }

        // --- NEW: AUTO-LOCK CONTINUOUS TRACING ---
        if (_hoveredEdge != null)
        {
            Vertex opposite = _hoveredEdge.GetOppositeVertex(currentTip);

            // If the magnetic projection has reached the absolute end of this road block
            // (meaning the user's cursor is pulling past the intersection)
            if (Vector3.Distance(_hoveredPoint, opposite.transform.position) <= 1f)
            {
                _activeEdges.Add(_hoveredEdge);
                _activeVertices.Add(opposite);
                if (!opposite.isSelected) opposite.ToggleSelection();

                _hoveredEdge = null; // Clear so we don't draw it twice

                // Recursively call this method instantly!
                // This allows the engine to chain together dozens of tiny roundabout segments
                // in a single frame so the visual line never lags behind your fast mouse movements.
                UpdateDragPreview(liveMousePosition);
                return;
            }
        }

        DrawPreviewLine();
    }

    public void CommitDragWaypoint(Vector3 releasePosition)
    {
        if (_hoveredEdge == null) return;

        Vertex currentTip = _activeVertices.Last();
        Vertex opposite = _hoveredEdge.GetOppositeVertex(currentTip);

        if (Vector3.Distance(_hoveredPoint, opposite.transform.position) < nodeSnapDistance)
        {
            _activeEdges.Add(_hoveredEdge);
            _activeVertices.Add(opposite);
            if (!opposite.isSelected) opposite.ToggleSelection();
        }
        else
        {
            Vertex newStation = MapGenerator.Instance.SplitEdge(_hoveredEdge, _hoveredPoint);
            Edge connectingEdge = newStation.connectedEdges.Find(e => e.GetOppositeVertex(newStation) == currentTip);

            if (connectingEdge != null)
            {
                _activeEdges.Add(connectingEdge);
                _activeVertices.Add(newStation);
                newStation.ToggleSelection(); // Highlight the brand new mid-road stop!
            }
        }
        ClearPreviewData();
    }

    // ==========================================
    // FINALIZATION & VISUALS
    // ==========================================

    public void FinishRoute()
    {
        foreach (Vertex v in _activeVertices)
        {
            if (v.isSelected) v.ToggleSelection();
        }

        if (_activeEdges.Count > 0) SpawnPermanentRoute();

        _activeVertices.Clear();
        _activeEdges.Clear();
        ClearPreviewData();
        _previewLine.positionCount = 0;
    }

    private void DrawPreviewLine()
    {
        List<Vector3> pts = BuildBaseWaypoints(_activeVertices, _activeEdges);

        // Append A* Ghost Line
        if (_previewEdges.Count > 0)
        {
            pts.AddRange(BuildBaseWaypoints(_previewVertices, _previewEdges).Skip(1));
        }
        // Append Magnetic Trace
        else if (_hoveredEdge != null)
        {
            List<Vector3> edgePts = new List<Vector3>(_hoveredEdge.waypoints);
            if (_activeVertices.Last() == _hoveredEdge.vertexB) edgePts.Reverse();

            for (int i = 1; i <= _hoveredSegmentIndex; i++) pts.Add(edgePts[i]);
            pts.Add(_hoveredPoint);
        }

        _previewLine.positionCount = pts.Count;
        _previewLine.SetPositions(pts.ToArray());
    }

    private List<Vector3> BuildBaseWaypoints(List<Vertex> vertices, List<Edge> edges)
    {
        List<Vector3> allPoints = new List<Vector3>();
        if (vertices.Count == 0) return allPoints;

        allPoints.Add(vertices[0].transform.position);

        for (int i = 0; i < edges.Count; i++)
        {
            Edge edge = edges[i];
            Vertex currentStart = vertices[i];

            List<Vector3> edgePoints = new List<Vector3>(edge.waypoints);
            if (edge.vertexB == currentStart) edgePoints.Reverse();

            for (int j = 1; j < edgePoints.Count; j++) allPoints.Add(edgePoints[j]);
        }
        return allPoints;
    }

    // --- Helpers ---

    private Edge GetConnectingEdge(Vertex a, Vertex b)
    {
        return a.connectedEdges.Find(e => e.GetOppositeVertex(a) == b);
    }

    private void ClearPreviewData()
    {
        _lastAStarTarget = null;
        _previewVertices.Clear();
        _previewEdges.Clear();
        _hoveredEdge = null;
    }

    private float GetClosestPointOnEdge(Edge edge, Vertex startVertex, Vector3 mousePos, out Vector3 closestPoint, out int segmentIndex)
    {
        List<Vector3> pts = new List<Vector3>(edge.waypoints);
        if (startVertex == edge.vertexB) pts.Reverse();

        float minDist = float.MaxValue;
        closestPoint = Vector3.zero;
        segmentIndex = -1;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];

            Vector3 lineDir = (b - a).normalized;
            float lineLen = Vector3.Distance(a, b);
            float dot = Mathf.Clamp(Vector3.Dot(mousePos - a, lineDir), 0f, lineLen);
            Vector3 proj = a + lineDir * dot;

            float dist = Vector3.Distance(mousePos, proj);
            if (dist < minDist)
            {
                minDist = dist;
                closestPoint = proj;
                segmentIndex = i;
            }
        }
        return minDist;
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

        List<Vector3> finalPoints = BuildBaseWaypoints(_activeVertices, _activeEdges);
        lr.positionCount = finalPoints.Count;
        lr.SetPositions(finalPoints.ToArray());
    }
}