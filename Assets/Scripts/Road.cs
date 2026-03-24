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
}
