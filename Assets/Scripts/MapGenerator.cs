using UnityEngine;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Data")]
    public TextAsset geoJsonFile;

    [Header("Rendering Settings")]
    public Material lineMaterial;
    public float roadWidth = 8f;

    [Header("Simulation Objects")]
    public GameObject intersectionNodePrefab;
    public float nodeMergeRadius = 100f;

    private float mapScale = 111000f;
    private Vector2 originLonLat;
    private bool isOriginSet = false;

    private Dictionary<string, NodeData> intersectionMap = new Dictionary<string, NodeData>();

    private List<RoadData> allRoads = new List<RoadData>();
    private List<TransitNode> allSpawnedNodes = new List<TransitNode>();

    private class NodeData
    {
        public Vector3 worldPosition;
        // Tracks unique physical OSM segments
        public HashSet<RoadData> connectedSegments = new HashSet<RoadData>();
        // Tracks unique Street Names (to find major intersections)
        public HashSet<string> uniqueRoadNames = new HashSet<string>();
        public TransitNode spawnedNode;

        public NodeData(Vector3 pos) { worldPosition = pos; }
    }

    private class Waypoint
    {
        public Vector3 position;
        public string key;
    }

    private class RoadData
    {
        public string name;
        public List<Waypoint> waypoints = new List<Waypoint>();
    }

    void Start()
    {
        if (geoJsonFile != null)
        {
            GenerateRoads();
            SpawnIntersections();
            BuildNetworkGraph();
        }
    }

    void GenerateRoads()
    {
        JObject mapData = JObject.Parse(geoJsonFile.text);
        JArray features = (JArray)mapData["features"];

        int roadCount = 0;

        foreach (JToken feature in features)
        {
            JToken geometry = feature["geometry"];

            if (geometry["type"].ToString() == "LineString")
            {
                string roadName = $"Road_{roadCount}";
                JToken props = feature["properties"];

                if (props != null)
                {
                    if (props["name:en"] != null) roadName = props["name:en"].ToString();
                    else if (props["name"] != null) roadName = props["name"].ToString();
                }

                if (roadName == $"Road_{roadCount}" && feature["id"] != null)
                {
                    roadName = $"Unnamed_{feature["id"].ToString()}";
                }

                RoadData newRoad = new RoadData { name = roadName };
                allRoads.Add(newRoad);

                JArray coordinates = (JArray)geometry["coordinates"];
                DrawRoadAndTrackNodes(coordinates, newRoad);
                roadCount++;
            }
        }
    }

    void DrawRoadAndTrackNodes(JArray coordinates, RoadData currentRoad)
    {
        GameObject roadObj = new GameObject(currentRoad.name);
        roadObj.transform.SetParent(this.transform);

        LineRenderer lr = roadObj.AddComponent<LineRenderer>();
        lr.positionCount = coordinates.Count;
        lr.startWidth = roadWidth;
        lr.endWidth = roadWidth;
        lr.useWorldSpace = false;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;

        if (lineMaterial != null) lr.material = lineMaterial;

        for (int i = 0; i < coordinates.Count; i++)
        {
            float lon = (float)coordinates[i][0];
            float lat = (float)coordinates[i][1];

            if (!isOriginSet)
            {
                originLonLat = new Vector2(lon, lat);
                isOriginSet = true;
            }

            float x = (lon - originLonLat.x) * mapScale * Mathf.Cos(originLonLat.y * Mathf.Deg2Rad);
            float y = (lat - originLonLat.y) * mapScale;

            Vector3 worldPos = new Vector3(x, y, 0f);
            lr.SetPosition(i, worldPos);

            string pointKey = $"{Math.Round(lon, 5)}_{Math.Round(lat, 5)}";

            currentRoad.waypoints.Add(new Waypoint { position = worldPos, key = pointKey });

            if (!intersectionMap.ContainsKey(pointKey))
            {
                intersectionMap[pointKey] = new NodeData(worldPos);
            }

            // --- THE FIX IS HERE ---
            // Log both the exact physical segment AND the street name
            intersectionMap[pointKey].connectedSegments.Add(currentRoad);
            intersectionMap[pointKey].uniqueRoadNames.Add(currentRoad.name);
        }
    }

    void SpawnIntersections()
    {
        if (intersectionNodePrefab == null) return;

        GameObject nodeContainer = new GameObject("Transit_Nodes");
        nodeContainer.transform.SetParent(this.transform);

        Dictionary<Vector3, TransitNode> spawnedClusters = new Dictionary<Vector3, TransitNode>();
        int nodeCount = 0;

        foreach (var kvp in intersectionMap)
        {
            NodeData data = kvp.Value;

            // Major = Two differently named streets cross
            bool isMajorIntersection = data.uniqueRoadNames.Count > 1;
            // Minor = A single street was split into two segments by OSM
            bool isSegmentJoin = data.connectedSegments.Count > 1;

            if (isMajorIntersection || isSegmentJoin)
            {
                TransitNode matchedNode = null;

                foreach (var cluster in spawnedClusters)
                {
                    if (Vector3.Distance(data.worldPosition, cluster.Key) < nodeMergeRadius)
                    {
                        matchedNode = cluster.Value;

                        // Upgrade logic: If we previously clustered a hidden minor node here,
                        // but this new coordinate is a Major crossing, turn the visuals back on!
                        if (isMajorIntersection && !matchedNode.GetComponent<SpriteRenderer>().enabled)
                        {
                            matchedNode.GetComponent<SpriteRenderer>().enabled = true;
                            matchedNode.GetComponent<Collider2D>().enabled = true;
                        }
                        break;
                    }
                }

                if (matchedNode == null)
                {
                    GameObject newNode = Instantiate(intersectionNodePrefab, data.worldPosition, Quaternion.identity);
                    newNode.transform.SetParent(nodeContainer.transform);

                    // Rename it so you can see it in the hierarchy
                    newNode.name = isMajorIntersection ? $"Node_Major_{nodeCount}" : $"Node_HiddenRoute_{nodeCount}";
                    nodeCount++;

                    matchedNode = newNode.GetComponent<TransitNode>();

                    // --- THE FIX IS HERE ---
                    // If it is just stitching two pieces of Weizmann St together, hide it from the player!
                    if (!isMajorIntersection)
                    {
                        newNode.GetComponent<SpriteRenderer>().enabled = false;
                        newNode.GetComponent<Collider2D>().enabled = false;
                    }

                    spawnedClusters[data.worldPosition] = matchedNode;
                    allSpawnedNodes.Add(matchedNode);
                }

                data.spawnedNode = matchedNode;
            }
        }
    }

    void BuildNetworkGraph()
    {
        foreach (RoadData road in allRoads)
        {
            TransitNode lastNode = null;
            List<Vector3> currentSegment = new List<Vector3>();

            foreach (Waypoint wp in road.waypoints)
            {
                currentSegment.Add(wp.position);

                TransitNode hitNode = null;
                if (intersectionMap.ContainsKey(wp.key))
                {
                    hitNode = intersectionMap[wp.key].spawnedNode;
                }

                if (hitNode != null)
                {
                    if (lastNode != null && lastNode != hitNode)
                    {
                        if (!lastNode.neighbors.Contains(hitNode)) lastNode.neighbors.Add(hitNode);
                        if (!hitNode.neighbors.Contains(lastNode)) hitNode.neighbors.Add(lastNode);

                        List<Vector3> finalCurve = new List<Vector3>(currentSegment);
                        finalCurve[0] = lastNode.transform.position;
                        finalCurve[finalCurve.Count - 1] = hitNode.transform.position;

                        lastNode.pathGeometry[hitNode] = finalCurve;

                        List<Vector3> reverseCurve = new List<Vector3>(finalCurve);
                        reverseCurve.Reverse();
                        hitNode.pathGeometry[lastNode] = reverseCurve;
                    }

                    lastNode = hitNode;
                    currentSegment.Clear();
                    currentSegment.Add(hitNode.transform.position);
                }
            }
        }
    }
}