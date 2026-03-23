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
    public GameObject vertexPrefab;

    private float mapScale = 111000f;
    private Vector2 originLonLat;
    private bool isOriginSet = false;

    // Tracks every spawned intersection so we don't spawn duplicates
    private Dictionary<string, Vertex> activeVertices = new Dictionary<string, Vertex>();

    // Graph containers to keep the hierarchy clean
    private Transform vertexContainer;
    private Transform edgeContainer;

    void Start()
    {
        if (geoJsonFile != null)
        {
            vertexContainer = new GameObject("Graph_Vertices").transform;
            vertexContainer.SetParent(this.transform);

            edgeContainer = new GameObject("Graph_Edges").transform;
            edgeContainer.SetParent(this.transform);

            GenerateSpatialGraph();
        }
        else
        {
            Debug.LogError("No GeoJSON file assigned!");
        }
    }

    void GenerateSpatialGraph()
    {
        JObject mapData = JObject.Parse(geoJsonFile.text);
        JArray features = (JArray)mapData["features"];

        // --- PASS 1: THE SURVEYOR ---
        Dictionary<string, int> coordinateOccurrences = new Dictionary<string, int>();
        // Track the names of streets touching each coordinate to classify the vertex
        Dictionary<string, HashSet<string>> coordinateStreetNames = new Dictionary<string, HashSet<string>>();

        foreach (JToken feature in features)
        {
            if (feature["geometry"]["type"].ToString() == "LineString")
            {
                JArray coordinates = (JArray)feature["geometry"]["coordinates"];

                // Grab the English name first, fallback to Hebrew or Unnamed
                string englishName = feature["properties"]["name:en"]?.ToString();
                string localName = feature["properties"]["name"]?.ToString();
                string roadName = !string.IsNullOrEmpty(englishName) ? englishName : (localName ?? "Unnamed Road");

                for (int i = 0; i < coordinates.Count; i++)
                {
                    string key = GetCoordinateKey(coordinates[i]);

                    if (!coordinateOccurrences.ContainsKey(key))
                    {
                        coordinateOccurrences[key] = 0;
                        coordinateStreetNames[key] = new HashSet<string>();
                    }

                    coordinateOccurrences[key]++;
                    coordinateStreetNames[key].Add(roadName);

                    if (i == 0 || i == coordinates.Count - 1)
                    {
                        coordinateOccurrences[key] += 2;
                    }
                }
            }
        }

        // --- PASS 2: THE BUILDER ---
        int edgeCount = 0;

        foreach (JToken feature in features)
        {
            if (feature["geometry"]["type"].ToString() == "LineString")
            {
                RoadProperties props = RoadProperties.ParseFromGeoJSON(feature["properties"]);
                JArray coordinates = (JArray)feature["geometry"]["coordinates"];

                Vertex lastVertex = null;
                List<Vector3> currentEdgeWaypoints = new List<Vector3>();

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

                    currentEdgeWaypoints.Add(worldPos);
                    string key = GetCoordinateKey(coordinates[i]);

                    if (coordinateOccurrences[key] > 1)
                    {
                        Vertex currentVertex;
                        if (activeVertices.ContainsKey(key))
                        {
                            currentVertex = activeVertices[key];
                        }
                        else
                        {
                            GameObject vObj = Instantiate(vertexPrefab, worldPos, Quaternion.identity, vertexContainer);

                            // Label it in the Hierarchy so you know what the engine is doing
                            bool isMajorIntersection = coordinateStreetNames[key].Count > 1;
                            vObj.name = isMajorIntersection ? $"Vertex_Major_{activeVertices.Count}" : $"Vertex_Structural_{activeVertices.Count}";

                            currentVertex = vObj.GetComponent<Vertex>();
                            activeVertices[key] = currentVertex;
                        }

                        if (lastVertex != null && lastVertex != currentVertex)
                        {
                            CreateEdge(lastVertex, currentVertex, currentEdgeWaypoints, props, edgeCount);
                            edgeCount++;
                        }

                        lastVertex = currentVertex;
                        currentEdgeWaypoints.Clear();
                        currentEdgeWaypoints.Add(worldPos);
                    }
                }
            }
        }

        Debug.Log($"Spatial Graph Generated! Vertices: {activeVertices.Count} | Edges: {edgeCount}");
    }
    private void CreateEdge(Vertex vA, Vertex vB, List<Vector3> waypoints, RoadProperties props, int index)
    {
        // Dynamically build the Edge object
        GameObject edgeObj = new GameObject($"Edge_{index}_{props.streetName}");
        edgeObj.transform.SetParent(edgeContainer);

        Edge newEdge = edgeObj.AddComponent<Edge>();

        // Pass the material from the MapGenerator down to the LineRenderer
        LineRenderer lr = edgeObj.GetComponent<LineRenderer>();
        if (lineMaterial != null) lr.material = lineMaterial;

        // Initialize the graph logic and physical colliders
        newEdge.Initialize(vA, vB, waypoints, props, roadWidth);
    }

    // Rounds the coordinate to ~1 meter precision to act as a mathematical dictionary key
    private string GetCoordinateKey(JToken coord)
    {
        double lon = Math.Round((double)coord[0], 5);
        double lat = Math.Round((double)coord[1], 5);
        return $"{lon}_{lat}";
    }
}