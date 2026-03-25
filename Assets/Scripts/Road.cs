using UnityEngine;
using System.Collections.Generic;

public class Road : MonoBehaviour
{
    [Header("OSM Data")]
    public long osmWayId;
    public string roadName;
    public float speedLimit;
    public bool isOneWay;

    [Header("Graph References")]
    public Vertex vertexA;
    public Vertex vertexB;

    [Header("Geometry")]
    public List<Vector3> centerlineWaypoints = new List<Vector3>();

    public Vertex GetOppositeNode(Vertex node)
    {
        // the variables that hold the two ends of your road!
        if (vertexA == node) return vertexB;
        if (vertexB == node) return vertexA;

        return null;
    }
}
