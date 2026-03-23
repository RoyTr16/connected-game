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
        // This will hold the massive list of every single curve coordinate for the whole trip
        List<Vector3> finalWaypoints = new List<Vector3>();

        // Loop through the A* path
        for (int i = 0; i < path.Count - 1; i++)
        {
            TransitNode current = path[i];
            TransitNode next = path[i + 1];

            // If we have the exact road curve saved, use it!
            if (current.pathGeometry.ContainsKey(next))
            {
                List<Vector3> curve = current.pathGeometry[next];

                // Add all points EXCEPT the very last one (to prevent drawing the intersection twice)
                for (int j = 0; j < curve.Count - 1; j++)
                {
                    finalWaypoints.Add(curve[j]);
                }
            }
            else
            {
                // Fallback: Just draw a straight line if something went wrong
                finalWaypoints.Add(current.transform.position);
            }
        }

        // Cap off the line with the exact position of the final destination node
        finalWaypoints.Add(path[path.Count - 1].transform.position);

        // --- Standard Line Rendering ---
        GameObject routeObj = new GameObject("TransitRoute");
        routeObj.transform.SetParent(this.transform);

        LineRenderer lr = routeObj.AddComponent<LineRenderer>();
        lr.positionCount = finalWaypoints.Count;

        for (int i = 0; i < finalWaypoints.Count; i++)
        {
            lr.SetPosition(i, finalWaypoints[i]);
        }

        lr.startWidth = routeWidth;
        lr.endWidth = routeWidth;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;

        if (routeMaterial != null) lr.material = routeMaterial;

        lr.startColor = routeColor;
        lr.endColor = routeColor;
    }
}