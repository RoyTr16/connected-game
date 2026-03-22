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

    // NEW: Memory lists to build our Graph
    private List<RoadData> allRoads = new List<RoadData>();
    private List<TransitNode> allSpawnedNodes = new List<TransitNode>();

    private class NodeData
    {
        public Vector3 worldPosition;
        public HashSet<string> connectedRoads = new HashSet<string>();
        public NodeData(Vector3 pos) { worldPosition = pos; }
    }

    // NEW: Remembers the physical path of every road
    private class RoadData
    {
        public string name;
        public List<Vector3> waypoints = new List<Vector3>();
    }

    void Start()
    {
        if (geoJsonFile != null)
        {
            GenerateRoads();
            SpawnIntersections();

            // NEW: Run the graph linking algorithm
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
                    // 1. Try to get the explicit English name first
                    if (props["name:en"] != null)
                    {
                        roadName = props["name:en"].ToString();
                    }
                    // 2. Fall back to the default local name (Hebrew) if no English exists
                    else if (props["name"] != null)
                    {
                        roadName = props["name"].ToString();
                    }
                }

                // 3. Fall back to the OSM ID if it's completely unnamed
                if (roadName == $"Road_{roadCount}" && feature["id"] != null)
                {
                    roadName = $"Unnamed_{feature["id"].ToString()}";
                }

                // Track the new road
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

            // Save the exact waypoint to the road memory
            currentRoad.waypoints.Add(worldPos);

            string pointKey = $"{Math.Round(lon, 5)}_{Math.Round(lat, 5)}";

            if (!intersectionMap.ContainsKey(pointKey))
            {
                intersectionMap[pointKey] = new NodeData(worldPos);
            }

            intersectionMap[pointKey].connectedRoads.Add(currentRoad.name);
        }
    }

    void SpawnIntersections()
    {
        if (intersectionNodePrefab == null) return;

        GameObject nodeContainer = new GameObject("Transit_Nodes");
        nodeContainer.transform.SetParent(this.transform);

        List<Vector3> clusteredNodes = new List<Vector3>();

        foreach (var kvp in intersectionMap)
        {
            NodeData data = kvp.Value;

            if (data.connectedRoads.Count > 1)
            {
                bool isTooClose = false;
                foreach (Vector3 existingNode in clusteredNodes)
                {
                    if (Vector3.Distance(data.worldPosition, existingNode) < nodeMergeRadius)
                    {
                        isTooClose = true;
                        break;
                    }
                }

                if (!isTooClose)
                    clusteredNodes.Add(data.worldPosition);
            }
        }

        int nodeCount = 0;
        foreach (Vector3 pos in clusteredNodes)
        {
            GameObject newNode = Instantiate(intersectionNodePrefab, pos, Quaternion.identity);
            newNode.transform.SetParent(nodeContainer.transform);
            newNode.name = $"Node_{nodeCount}";

            // Save the spawned node so the graph can find it later
            TransitNode tNode = newNode.GetComponent<TransitNode>();
            if (tNode != null) allSpawnedNodes.Add(tNode);

            nodeCount++;
        }
    }

    // --- THE GRAPH ALGORITHM ---
    void BuildNetworkGraph()
    {
        foreach (RoadData road in allRoads)
        {
            List<TransitNode> nodesOnThisRoad = new List<TransitNode>();

            // Walk down the exact path of the road
            foreach (Vector3 waypoint in road.waypoints)
            {
                // Check if this waypoint is sitting on top of a transit node
                foreach (TransitNode node in allSpawnedNodes)
                {
                    if (Vector3.Distance(waypoint, node.transform.position) <= nodeMergeRadius)
                    {
                        // Add it, but prevent adding the exact same node back-to-back
                        if (nodesOnThisRoad.Count == 0 || nodesOnThisRoad[nodesOnThisRoad.Count - 1] != node)
                        {
                            nodesOnThisRoad.Add(node);
                        }
                    }
                }
            }

            // Now we have an ordered list of every node on this specific street. Link them!
            for (int i = 0; i < nodesOnThisRoad.Count; i++)
            {
                TransitNode current = nodesOnThisRoad[i];

                if (i > 0)
                {
                    TransitNode prev = nodesOnThisRoad[i - 1];
                    if (!current.neighbors.Contains(prev)) current.neighbors.Add(prev);
                }

                if (i < nodesOnThisRoad.Count - 1)
                {
                    TransitNode next = nodesOnThisRoad[i + 1];
                    if (!current.neighbors.Contains(next)) current.neighbors.Add(next);
                }
            }
        }
        Debug.Log("Transit Network Graph successfully linked!");
    }
}