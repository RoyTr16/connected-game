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
        // Case 1: The player clicked the exact same node twice. (Deselect it)
        if (_firstSelectedNode == clickedNode)
        {
            clickedNode.ToggleSelection(); // Turns it back to green
            _firstSelectedNode = null;     // Clear our memory
            return;
        }

        // Case 2: This is the very first node the player is clicking
        if (_firstSelectedNode == null)
        {
            _firstSelectedNode = clickedNode;
            _firstSelectedNode.ToggleSelection(); // Turns it yellow
        }
        // Case 3: We already have a first node, so this is the destination!
        else
        {
            // Draw the physical route line
            DrawRouteLine(_firstSelectedNode.transform.position, clickedNode.transform.position);

            // Turn the first node back to its original color (since the connection is made)
            _firstSelectedNode.ToggleSelection();

            // Clear our memory so the player can draw a brand new route
            _firstSelectedNode = null;
        }
    }

    private void DrawRouteLine(Vector3 startPos, Vector3 endPos)
    {
        // Create a new empty GameObject to hold the line
        GameObject routeObj = new GameObject("TransitRoute");
        routeObj.transform.SetParent(this.transform); // Keep the hierarchy clean

        // Add and configure the LineRenderer
        LineRenderer lr = routeObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, startPos);
        lr.SetPosition(1, endPos);

        lr.startWidth = routeWidth;
        lr.endWidth = routeWidth;
        lr.numCapVertices = 4; // Rounded ends

        if (routeMaterial != null) lr.material = routeMaterial;

        // Apply the color (Requires a material like Sprites-Default to show up)
        lr.startColor = routeColor;
        lr.endColor = routeColor;
    }
}