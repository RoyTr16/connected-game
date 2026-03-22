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
    public float nodeMergeRadius = 100f; // Kept your updated radius

    private float mapScale = 111000f;
    private Vector2 originLonLat;
    private bool isOriginSet = false;

    private Dictionary<string, NodeData> intersectionMap = new Dictionary<string, NodeData>();

    private class NodeData
    {
        public Vector3 worldPosition;
        // Using a HashSet means if we add "Weizmann St" twice, it still only counts as 1 road.
        public HashSet<string> connectedRoads = new HashSet<string>();

        public NodeData(Vector3 pos)
        {
            worldPosition = pos;
        }
    }

    void Start()
    {
        if (geoJsonFile != null)
        {
            GenerateRoads();
            SpawnIntersections();
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
                // --- THE FIX IS HERE ---
                // Try to get the real street name. If it doesn't have one, fall back to its OSM ID.
                string roadName = $"Road_{roadCount}";
                JToken props = feature["properties"];

                if (props != null && props["name"] != null)
                {
                    roadName = props["name"].ToString();
                }
                else if (feature["id"] != null)
                {
                    // Use the OSM ID for unnamed roads so we can still track them uniquely
                    roadName = $"Unnamed_{feature["id"].ToString()}";
                }
                // -----------------------

                JArray coordinates = (JArray)geometry["coordinates"];
                DrawRoadAndTrackNodes(coordinates, roadName);
                roadCount++;
            }
        }
    }

    void DrawRoadAndTrackNodes(JArray coordinates, string roadName)
    {
        GameObject roadObj = new GameObject(roadName);
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

            if (!intersectionMap.ContainsKey(pointKey))
            {
                intersectionMap[pointKey] = new NodeData(worldPos);
            }

            // Because we now pass the REAL name (e.g., "Weizmann Street"), the HashSet
            // will ignore it if "Weizmann Street" is already registered to this coordinate!
            intersectionMap[pointKey].connectedRoads.Add(roadName);
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
                {
                    clusteredNodes.Add(data.worldPosition);
                }
            }
        }

        int nodeCount = 0;
        foreach (Vector3 pos in clusteredNodes)
        {
            GameObject newNode = Instantiate(intersectionNodePrefab, pos, Quaternion.identity);
            newNode.transform.SetParent(nodeContainer.transform);
            newNode.name = $"Node_{nodeCount}";
            nodeCount++;
        }

        Debug.Log($"Successfully mapped {nodeCount} consolidated intersections.");
    }
}