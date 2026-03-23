using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer), typeof(EdgeCollider2D))]
public class Edge : MonoBehaviour
{
    [Header("Graph Data")]
    public Vertex vertexA;
    public Vertex vertexB;

    // This holds all the speed limit and one-way data we parsed earlier!
    public RoadProperties properties;

    [Header("Geometry")]
    public List<Vector3> waypoints = new List<Vector3>();

    private LineRenderer _lineRenderer;
    private EdgeCollider2D _edgeCollider;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _edgeCollider = GetComponent<EdgeCollider2D>();
    }

    // The MapGenerator will call this to set up the road block
    public void Initialize(Vertex a, Vertex b, List<Vector3> points, RoadProperties props, float width)
    {
        vertexA = a;
        vertexB = b;
        waypoints = new List<Vector3>(points);
        properties = props;

        // Automatically tell the two intersections that this road connects to them
        if (vertexA != null) vertexA.AddEdge(this);
        if (vertexB != null) vertexB.AddEdge(this);

        UpdateVisuals(width);
        UpdateCollider();
    }

    // A fast way for the A* pathfinder to ask "Where does this road go?"
    public Vertex GetOppositeVertex(Vertex currentVertex)
    {
        if (currentVertex == vertexA) return vertexB;
        if (currentVertex == vertexB) return vertexA;
        return null;
    }

    private void UpdateVisuals(float width)
    {
        if (_lineRenderer == null || waypoints.Count == 0) return;

        _lineRenderer.positionCount = waypoints.Count;
        _lineRenderer.SetPositions(waypoints.ToArray());
        _lineRenderer.startWidth = width;
        _lineRenderer.endWidth = width;
        _lineRenderer.numCapVertices = 4;
        _lineRenderer.numCornerVertices = 4;
    }

    private void UpdateCollider()
    {
        if (_edgeCollider == null || waypoints.Count < 2) return;

        // Unity Physics trick: EdgeCollider2D requires 2D coordinates in "Local" space.
        // Since our OSM map waypoints are in global 3D space, we have to convert them.
        List<Vector2> localPoints = new List<Vector2>();
        foreach (Vector3 worldPoint in waypoints)
        {
            localPoints.Add(transform.InverseTransformPoint(worldPoint));
        }

        _edgeCollider.SetPoints(localPoints);

        // Make the invisible physics collider exactly as thick as the visual line
        // so the player's mouse easily clicks it.
        _edgeCollider.edgeRadius = _lineRenderer.startWidth / 2f;
    }

    // --- City Editing ---
    // If the player bulldozes this road, it automatically cleans up the graph!
    private void OnDestroy()
    {
        if (vertexA != null) vertexA.RemoveEdge(this);
        if (vertexB != null) vertexB.RemoveEdge(this);
    }
}