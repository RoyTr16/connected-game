using System.Collections.Generic;
using UnityEngine;

public class LineManager : MonoBehaviour
{
    // A simple Singleton so other scripts can easily access this
    public static LineManager Instance { get; private set; }

    [Header("Route Visuals")]
    public Material routeMaterial;
    public float routeWidth = 6f;
    public Color routeColor = Color.blue;

    // This remembers the first node we clicked
    private TransitNode _firstSelectedNode;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void OnNodeClicked(TransitNode clickedNode)
    {
        if (_firstSelectedNode == clickedNode)
        {
            clickedNode.ToggleSelection();
            _firstSelectedNode = null;
            return;
        }

        if (_firstSelectedNode == null)
        {
            _firstSelectedNode = clickedNode;
            _firstSelectedNode.ToggleSelection();
        }
        else
        {
            // --- NEW: Ask the Pathfinder for the route ---
            List<TransitNode> path = Pathfinder.FindPath(_firstSelectedNode, clickedNode);

            if (path != null && path.Count > 0)
            {
                DrawRouteLine(path);
            }

            _firstSelectedNode.ToggleSelection();
            _firstSelectedNode = null;
        }
    }

    // --- NEW: Draw along multiple waypoints instead of just two ---
    private void DrawRouteLine(List<TransitNode> path)
    {
        GameObject routeObj = new GameObject("TransitRoute");
        routeObj.transform.SetParent(this.transform);

        LineRenderer lr = routeObj.AddComponent<LineRenderer>();

        // Give the LineRenderer exactly as many points as our path has
        lr.positionCount = path.Count;

        for (int i = 0; i < path.Count; i++)
        {
            lr.SetPosition(i, path[i].transform.position);
        }

        lr.startWidth = routeWidth;
        lr.endWidth = routeWidth;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4; // Rounds out the sharp turns on the street corners

        if (routeMaterial != null) lr.material = routeMaterial;

        lr.startColor = routeColor;
        lr.endColor = routeColor;
    }
}