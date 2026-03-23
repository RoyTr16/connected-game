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
    [Tooltip("How far the mouse can drift off the road")]
    public float edgeSnapDistance = 30f;

    private bool _isRouting = false;
    private List<Vertex> _activeVertices = new List<Vertex>();
    private List<Edge> _activeEdges = new List<Edge>();

    // Variables for the partial tracing UX
    private Edge _hoveredEdge;
    private Vector3 _hoveredPoint;

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

        // If the user let go, but they were halfway down a road,
        // this is where we will eventually trigger the "Split Edge & Create Bus Stop" logic!
        if (_hoveredEdge != null)
        {
            Debug.Log($"Dropped route in the middle of {_hoveredEdge.properties.streetName}. Ready to create a station here!");
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

        // --- 1. UNDO LOGIC: Erase the previous road if we drag backwards ---
        if (_activeVertices.Count > 1)
        {
            Vertex previousTip = _activeVertices[_activeVertices.Count - 2];
            if (Vector3.Distance(liveMousePosition, previousTip.transform.position) < nodeSnapDistance)
            {
                _activeEdges.RemoveAt(_activeEdges.Count - 1);
                _activeVertices.RemoveAt(_activeVertices.Count - 1);
                _hoveredEdge = null;
                return; // Stop processing for this frame to prevent jitter
            }
        }

        // --- 2. FIND THE CLOSEST ROAD ---
        _hoveredEdge = null;
        float closestDist = edgeSnapDistance;

        foreach (Edge edge in currentTip.connectedEdges)
        {
            // Don't trace backwards down the road we just locked in
            if (_activeEdges.Count > 0 && edge == _activeEdges.Last()) continue;

            Collider2D col = edge.GetComponent<Collider2D>();
            if (col == null) continue;

            // Find the exact projection of our mouse onto the physical asphalt
            Vector3 pointOnRoad = col.ClosestPoint(liveMousePosition);
            float distToMouse = Vector3.Distance(liveMousePosition, pointOnRoad);

            if (distToMouse < closestDist)
            {
                closestDist = distToMouse;
                _hoveredEdge = edge;
                _hoveredPoint = pointOnRoad;
            }
        }

        // --- 3. LOCKING LOGIC ---
        if (_hoveredEdge != null)
        {
            Vertex opposite = _hoveredEdge.GetOppositeVertex(currentTip);

            // Did our mouse reach the end of the block? Lock it in!
            if (Vector3.Distance(_hoveredPoint, opposite.transform.position) < nodeSnapDistance)
            {
                _activeEdges.Add(_hoveredEdge);
                _activeVertices.Add(opposite);
                _hoveredEdge = null; // Clear hover state so we don't draw it twice
            }
        }

        // --- 4. DRAW THE LIVE LINE ---
        DrawPreview();
    }

    private void DrawPreview()
    {
        List<Vector3> previewPoints = BuildRouteWaypoints();

        // If we are currently dragging halfway down a road, append exactly that curve
        if (_hoveredEdge != null && _activeVertices.Count > 0)
        {
            Vertex tip = _activeVertices.Last();
            List<Vector3> partialCurve = GetPartialCurve(_hoveredEdge, tip, _hoveredPoint);

            // Skip the first point of the curve to avoid duplicating the intersection coordinate
            for (int i = 1; i < partialCurve.Count; i++)
            {
                previewPoints.Add(partialCurve[i]);
            }
        }

        _previewLine.positionCount = previewPoints.Count;
        _previewLine.SetPositions(previewPoints.ToArray());
    }

    // --- NEW: Traces a road's waypoints and stops exactly at the mouse cursor ---
    private List<Vector3> GetPartialCurve(Edge edge, Vertex startVertex, Vector3 endPoint)
    {
        List<Vector3> pts = new List<Vector3>(edge.waypoints);
        if (startVertex == edge.vertexB) pts.Reverse();

        List<Vector3> partial = new List<Vector3>();
        partial.Add(pts[0]);

        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];

            // Math trick: If point C is on line segment AB, then Distance(A,C) + Distance(C,B) == Distance(A,B).
            float d1 = Vector3.Distance(a, endPoint);
            float d2 = Vector3.Distance(endPoint, b);
            float dAB = Vector3.Distance(a, b);

            // Using a 0.1f tolerance for floating point math errors
            if (Mathf.Abs((d1 + d2) - dAB) < 0.1f)
            {
                partial.Add(endPoint);
                break; // We found the exact segment the mouse is on, stop tracing!
            }
            else
            {
                partial.Add(b); // Not there yet, add the corner and keep tracing
            }
        }
        return partial;
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