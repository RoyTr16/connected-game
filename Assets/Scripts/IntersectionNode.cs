using System.Collections.Generic;
using UnityEngine;

public class IntersectionNode : MonoBehaviour
{
    [Tooltip("Drag the empty Socket GameObjects here")]
    public Transform[] socketTransforms;

    [Tooltip("The default width of roads coming out of this intersection")]
    public float defaultLaneWidth = 6f; // Adjust to your game's scale

    // The road builder will call this to get the exact connection data
    public List<RoadSocket> GetSockets()
    {
        List<RoadSocket> sockets = new List<RoadSocket>();
        foreach (Transform t in socketTransforms)
        {
            if (t != null)
            {
                // In Unity, the blue Z-axis is "forward", which perfectly matches t.forward
                sockets.Add(new RoadSocket(t.position, t.forward, defaultLaneWidth));
            }
        }
        return sockets;
    }

    // --- TDD VISUALIZATION ---
    // This draws the sockets in the Unity Editor so you can physically see if they are set up right!
    private void OnDrawGizmos()
    {
        if (socketTransforms == null) return;

        foreach (Transform t in socketTransforms)
        {
            if (t == null) continue;

            // Draw a green dot at the exact start position
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(t.position, 0.5f);

            // Draw a blue line pointing in the exact Forward direction
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(t.position, t.forward * 4f);
        }
    }
}