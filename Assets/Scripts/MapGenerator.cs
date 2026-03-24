using UnityEngine;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;

public class MapGenerator : MonoBehaviour
{
    public static MapGenerator Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

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
                            CreateEdge(lastVertex, currentVertex, currentEdgeWaypoints, props, $"_{edgeCount}");
                            edgeCount++;
                        }

                        lastVertex = currentVertex;
                        currentEdgeWaypoints.Clear();
                        currentEdgeWaypoints.Add(worldPos);
                    }
                }
            }
        }

        // Fire up the traffic engine now that the roads exist!
        if (TrafficManager.Instance != null)
        {
            TrafficManager.Instance.InitializeTraffic();
        }

        Debug.Log($"Spatial Graph Generated! Vertices: {activeVertices.Count} | Edges: {edgeCount}");
    }
    public Edge CreateEdge(Vertex vA, Vertex vB, List<Vector3> waypoints, RoadProperties props, string nameSuffix = "")
    {
        GameObject edgeObj = new GameObject($"Edge_{props.streetName}{nameSuffix}");
        edgeObj.transform.SetParent(edgeContainer);

        Edge newEdge = edgeObj.AddComponent<Edge>();

        LineRenderer lr = edgeObj.GetComponent<LineRenderer>();
        if (lineMaterial != null) lr.material = lineMaterial;

        newEdge.Initialize(vA, vB, waypoints, props, roadWidth);
        return newEdge;
    }

    // Rounds the coordinate to ~1 meter precision to act as a mathematical dictionary key
    private string GetCoordinateKey(JToken coord)
    {
        double lon = Math.Round((double)coord[0], 5);
        double lat = Math.Round((double)coord[1], 5);
        return $"{lon}_{lat}";
    }

    // ==========================================
    // DYNAMIC EDGE SPLITTING
    // ==========================================

    public Vertex SplitEdge(Edge originalEdge, Vector3 splitPoint)
    {
        List<Vector3> pts = originalEdge.waypoints;
        int splitIndex = 0;
        float minDist = float.MaxValue;

        // Find the exact curve segment to slice on the un-reversed list
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 proj = ProjectPointOnLineSegment(pts[i], pts[i + 1], splitPoint);
            float d = Vector3.Distance(splitPoint, proj);
            if (d < minDist)
            {
                minDist = d;
                splitIndex = i;
            }
        }

        // Slice into Part 1
        List<Vector3> part1 = new List<Vector3>();
        for (int i = 0; i <= splitIndex; i++) part1.Add(pts[i]);
        part1.Add(splitPoint);

        // Slice into Part 2
        List<Vector3> part2 = new List<Vector3>();
        part2.Add(splitPoint);
        for (int i = splitIndex + 1; i < pts.Count; i++) part2.Add(pts[i]);

        // 1. Spawn the brand new custom Vertex
        GameObject vObj = Instantiate(vertexPrefab, splitPoint, Quaternion.identity, vertexContainer);
        vObj.name = $"Vertex_CustomStop_{activeVertices.Count}";
        Vertex newVertex = vObj.GetComponent<Vertex>();

        // Make it green!
        newVertex.SetAsTransitStop(true);

        // Add it to our dictionary with a unique Guid so it's formally part of the network
        activeVertices[System.Guid.NewGuid().ToString()] = newVertex;

        // 2. Spawn the two new Edges
        Edge edge1 = CreateEdge(originalEdge.vertexA, newVertex, part1, originalEdge.properties, "_PartA");
        Edge edge2 = CreateEdge(newVertex, originalEdge.vertexB, part2, originalEdge.properties, "_PartB");

        // 3. Destroy the original unbroken road
        Destroy(originalEdge.gameObject);

        return newVertex;
    }

    private Vector3 ProjectPointOnLineSegment(Vector3 lineStart, Vector3 lineEnd, Vector3 point)
    {
        Vector3 lineDirection = lineEnd - lineStart;
        float lineLength = lineDirection.magnitude;
        lineDirection.Normalize();

        Vector3 projectLine = point - lineStart;
        float dotProduct = Vector3.Dot(projectLine, lineDirection);
        dotProduct = Mathf.Clamp(dotProduct, 0f, lineLength);

        return lineStart + lineDirection * dotProduct;
    }
}