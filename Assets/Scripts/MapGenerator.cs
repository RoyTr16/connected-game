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
    public float laneWidth = 3.0f;
    public float layerHeightOffset = -5.0f;

    [Header("Visuals")]
    [SerializeField] private Material asphaltMaterial;

    [Header("Debug Visualization")]
    public bool showDebugGizmos = false; // Default to false for performance

    [Header("Simulation Objects")]
    public GameObject vertexPrefab;
    [SerializeField] private bool showVertices = true;

    private float mapScale = 111000f;
    private Vector2 originLonLat;
    private bool isOriginSet = false;

    // OSM ID-keyed lookups
    private Dictionary<long, Vector3> osmNodePositions = new Dictionary<long, Vector3>();
    private Dictionary<long, Dictionary<string, string>> osmNodeTags = new Dictionary<long, Dictionary<string, string>>();
    private Dictionary<long, Vertex> osmNodeToVertex = new Dictionary<long, Vertex>();

    // Traffic rule storage (parsed from relations — not applied to GameObjects yet)
    public Dictionary<long, string> nodeTrafficRules { get; private set; } = new Dictionary<long, string>();
    public List<OsmTurnRestriction> turnRestrictions { get; private set; } = new List<OsmTurnRestriction>();

    private const float LaneOffset = 1.5f;

    // Graph containers to keep the hierarchy clean
    private Transform vertexContainer;
    private Transform roadContainer;

    // Tracks vertices by coordinate key for deduplication
    private Dictionary<string, Vertex> activeVertices = new Dictionary<string, Vertex>();

    void Start()
    {
        if (geoJsonFile != null)
        {
            vertexContainer = new GameObject("Graph_Vertices").transform;
            vertexContainer.SetParent(this.transform);

            roadContainer = new GameObject("Graph_Roads").transform;
            roadContainer.SetParent(this.transform);

            GenerateSpatialGraph();
            vertexContainer.gameObject.SetActive(showVertices);
        }
        else
        {
            Debug.LogError("No GeoJSON file assigned!");
        }
    }

    private void OnValidate()
    {
        if (vertexContainer != null)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (vertexContainer != null)
                    vertexContainer.gameObject.SetActive(showVertices);
            };
#endif
        }
    }

    // ==========================================
    // 3-PASS RAW OSM JSON PARSER
    // ==========================================

    void GenerateSpatialGraph()
    {
        JObject mapData = JObject.Parse(geoJsonFile.text);
        JArray elements = (JArray)mapData["elements"];

        // Separate elements by type for ordered processing
        List<JToken> nodeElements = new List<JToken>();
        List<JToken> wayElements = new List<JToken>();
        List<JToken> relationElements = new List<JToken>();

        foreach (JToken element in elements)
        {
            string type = element["type"].ToString();
            if (type == "node") nodeElements.Add(element);
            else if (type == "way") wayElements.Add(element);
            else if (type == "relation") relationElements.Add(element);
        }

        // =====================================================
        // PASS 1: NODES — Build coordinate lookup + traffic rules
        // =====================================================
        foreach (JToken node in nodeElements)
        {
            long id = (long)node["id"];
            double lat = (double)node["lat"];
            double lon = (double)node["lon"];

            if (!isOriginSet)
            {
                originLonLat = new Vector2((float)lon, (float)lat);
                isOriginSet = true;
            }

            float x = (float)((lon - originLonLat.x) * mapScale * Mathf.Cos(originLonLat.y * Mathf.Deg2Rad));
            float y = (float)((lat - originLonLat.y) * mapScale);
            Vector3 worldPos = new Vector3(x, y, 0f);
            osmNodePositions[id] = worldPos;

            // Parse node tags for traffic signals / stop signs
            JToken tags = node["tags"];
            if (tags != null)
            {
                Dictionary<string, string> tagDict = new Dictionary<string, string>();
                foreach (JProperty prop in tags.Children<JProperty>())
                    tagDict[prop.Name] = prop.Value.ToString();

                osmNodeTags[id] = tagDict;

                if (tagDict.TryGetValue("highway", out string highwayValue))
                {
                    if (highwayValue == "traffic_signals" || highwayValue == "stop")
                        nodeTrafficRules[id] = highwayValue;
                }
            }
        }

        // =====================================================
        // PASS 2: WAYS — Build Roads, Vertices, and Lanes
        // =====================================================
        // Pre-survey: count how many ways reference each node (for intersection detection)
        Dictionary<long, int> nodeWayCount = new Dictionary<long, int>();
        Dictionary<long, HashSet<string>> nodeStreetNames = new Dictionary<long, HashSet<string>>();

        foreach (JToken way in wayElements)
        {
            JToken tags = way["tags"];
            if (tags == null) continue;
            string highway = tags["highway"]?.ToString()?.ToLower();
            if (!IsDrivableHighway(highway)) continue;

            string englishName = tags["name:en"]?.ToString();
            string localName = tags["name"]?.ToString();
            string roadName = !string.IsNullOrEmpty(englishName) ? englishName : (localName ?? "Unnamed Road");

            JArray nodeIds = (JArray)way["nodes"];
            for (int i = 0; i < nodeIds.Count; i++)
            {
                long nodeId = (long)nodeIds[i];
                if (!nodeWayCount.ContainsKey(nodeId))
                {
                    nodeWayCount[nodeId] = 0;
                    nodeStreetNames[nodeId] = new HashSet<string>();
                }
                nodeWayCount[nodeId]++;
                nodeStreetNames[nodeId].Add(roadName);

                // Endpoints of ways are always potential vertices
                if (i == 0 || i == nodeIds.Count - 1)
                    nodeWayCount[nodeId] += 2;
            }
        }

        // Build the roads
        int roadCount = 0;
        int laneCount = 0;

        foreach (JToken way in wayElements)
        {
            long wayId = (long)way["id"];
            JToken tags = way["tags"];
            if (tags == null) continue;
            string highway = tags["highway"]?.ToString()?.ToLower();
            if (!IsDrivableHighway(highway)) continue;

            RoadProperties props = RoadProperties.ParseFromOsmTags(tags);
            JArray nodeIds = (JArray)way["nodes"];

            // Read the OSM layer tag for vertical separation (bridges, tunnels)
            int layer = 0;
            string layerStr = tags["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerStr))
                int.TryParse(layerStr, out layer);
            float zElevation = layer * layerHeightOffset;

            Vertex lastVertex = null;
            List<Vector3> currentWaypoints = new List<Vector3>();

            for (int i = 0; i < nodeIds.Count; i++)
            {
                long nodeId = (long)nodeIds[i];

                if (!osmNodePositions.TryGetValue(nodeId, out Vector3 worldPos))
                    continue; // Skip if the node wasn't in our export

                // Apply vertical offset for bridges/tunnels
                worldPos.z = zElevation;
                currentWaypoints.Add(worldPos);

                // Split at intersection nodes (referenced by multiple ways or at endpoints)
                if (nodeWayCount.ContainsKey(nodeId) && nodeWayCount[nodeId] > 1)
                {
                    Vertex currentVertex = GetOrCreateVertex(nodeId, worldPos, nodeStreetNames);

                    if (lastVertex != null && lastVertex != currentVertex && currentWaypoints.Count >= 2)
                    {
                        Road road = CreateRoad(lastVertex, currentVertex, currentWaypoints, props, $"_{roadCount}");
                        road.osmWayId = wayId;
                        laneCount += road.GetComponentsInChildren<LaneAuthoring>().Length;
                        roadCount++;
                    }

                    lastVertex = currentVertex;
                    currentWaypoints.Clear();
                    currentWaypoints.Add(worldPos);
                }
            }
        }

        // =====================================================
        // PASS 3: RELATIONS — Extract turn restrictions
        // =====================================================
        foreach (JToken relation in relationElements)
        {
            JToken tags = relation["tags"];
            if (tags == null) continue;

            string relationType = tags["type"]?.ToString();
            if (relationType != "restriction") continue;

            string restriction = tags["restriction"]?.ToString();
            if (string.IsNullOrEmpty(restriction)) continue;

            JArray members = (JArray)relation["members"];
            if (members == null) continue;

            long fromWayId = -1;
            long viaNodeId = -1;
            long toWayId = -1;

            foreach (JToken member in members)
            {
                string role = member["role"]?.ToString();
                string memberType = member["type"]?.ToString();
                long refId = (long)member["ref"];

                if (role == "from" && memberType == "way") fromWayId = refId;
                else if (role == "via" && memberType == "node") viaNodeId = refId;
                else if (role == "to" && memberType == "way") toWayId = refId;
            }

            if (fromWayId != -1 && viaNodeId != -1 && toWayId != -1)
            {
                turnRestrictions.Add(new OsmTurnRestriction
                {
                    fromWayId = fromWayId,
                    viaNodeId = viaNodeId,
                    toWayId = toWayId,
                    restrictionType = restriction
                });
            }
        }

        // =====================================================
        // POST-PARSE: Attach IntersectionAuthoring to all vertices
        // =====================================================
        foreach (var kvp in osmNodeToVertex)
        {
            long nodeId = kvp.Key;
            Vertex vertex = kvp.Value;

            IntersectionAuthoring ia = vertex.gameObject.AddComponent<IntersectionAuthoring>();
            ia.osmNodeId = nodeId;

            if (nodeTrafficRules.TryGetValue(nodeId, out string rule))
            {
                if (rule == "traffic_signals")
                    ia.controlType = IntersectionControlType.TrafficLight;
                else if (rule == "stop")
                    ia.controlType = IntersectionControlType.StopSign;
            }
        }

        // Fire up the traffic engine now that the roads exist!
        if (TrafficManager.Instance != null)
        {
            TrafficManager.Instance.InitializeTraffic();
        }

        Debug.Log($"OSM Graph Generated! Nodes: {osmNodePositions.Count} | Vertices: {activeVertices.Count} | Roads: {roadCount} | Lanes: {laneCount} | Traffic Rules: {nodeTrafficRules.Count} | Turn Restrictions: {turnRestrictions.Count}");
    }

    // ==========================================
    // VERTEX MANAGEMENT
    // ==========================================

    private Vertex GetOrCreateVertex(long osmNodeId, Vector3 worldPos, Dictionary<long, HashSet<string>> nodeStreetNames)
    {
        // Use the OSM node ID as the primary key
        if (osmNodeToVertex.TryGetValue(osmNodeId, out Vertex existing))
            return existing;

        // Also check coordinate-based dedup for split vertices
        string coordKey = $"{worldPos.x:F2}_{worldPos.y:F2}";
        if (activeVertices.TryGetValue(coordKey, out Vertex coordMatch))
        {
            osmNodeToVertex[osmNodeId] = coordMatch;
            return coordMatch;
        }

        GameObject vObj = Instantiate(vertexPrefab, worldPos, Quaternion.identity, vertexContainer);

        bool isMajor = nodeStreetNames.ContainsKey(osmNodeId) && nodeStreetNames[osmNodeId].Count > 1;
        vObj.name = isMajor ? $"Vertex_Major_{osmNodeId}" : $"Vertex_Structural_{osmNodeId}";

        Vertex vertex = vObj.GetComponent<Vertex>();
        vertex.osmNodeId = osmNodeId;

        osmNodeToVertex[osmNodeId] = vertex;
        activeVertices[coordKey] = vertex;

        return vertex;
    }

    private static bool IsDrivableHighway(string highway)
    {
        if (string.IsNullOrEmpty(highway)) return false;
        switch (highway)
        {
            case "motorway":
            case "motorway_link":
            case "trunk":
            case "trunk_link":
            case "primary":
            case "primary_link":
            case "secondary":
            case "secondary_link":
            case "tertiary":
            case "tertiary_link":
            case "residential":
            case "unclassified":
            case "living_street":
                return true;
            default:
                return false;
        }
    }
    public Road CreateRoad(Vertex vA, Vertex vB, List<Vector3> centerline, RoadProperties props, string nameSuffix = "")
    {
        // --- 1. Create the Road parent ---
        GameObject roadObj = new GameObject($"Road_{props.streetName}{nameSuffix}");
        roadObj.transform.SetParent(roadContainer);

        Road road = roadObj.AddComponent<Road>();
        road.roadName = props.streetName;
        road.speedLimit = props.maxSpeed;
        road.isOneWay = props.isOneWay;
        road.vertexA = vA;
        road.vertexB = vB;
        road.centerlineWaypoints = new List<Vector3>(centerline);

        // Tell the vertices this road connects to them
        if (vA != null) vA.AddRoad(road);
        if (vB != null) vB.AddRoad(road);

        // --- 2. Spawn LaneAuthoring children ---
        if (props.isOneWay)
        {
            // Determine the actual flow direction based on OSM reversed tag
            List<Vector3> flowWaypoints = props.isReversedOneWay
                ? ReverseWaypoints(centerline)
                : new List<Vector3>(centerline);

            CreateLaneChild(roadObj.transform, flowWaypoints, "Lane_0");
        }
        else
        {
            // Two-way: Lane 0 = forward flow, Lane 1 = reverse flow
            List<Vector3> forwardWaypoints = new List<Vector3>(centerline);
            List<Vector3> reverseWaypoints = ReverseWaypoints(centerline);

            CreateLaneChild(roadObj.transform, forwardWaypoints, "Lane_Fwd");
            CreateLaneChild(roadObj.transform, reverseWaypoints, "Lane_Rev");
        }

        // --- 3. Add a procedural 3D mesh per lane for rendering ---
        LaneAuthoring[] laneChildren = roadObj.GetComponentsInChildren<LaneAuthoring>();
        foreach (LaneAuthoring lane in laneChildren)
        {
            MeshFilter mf = lane.gameObject.AddComponent<MeshFilter>();
            MeshRenderer mr = lane.gameObject.AddComponent<MeshRenderer>();
            mf.mesh = RoadMeshBuilder.CreateRoadMesh(lane.waypoints, laneWidth);
            if (asphaltMaterial != null)
                mr.sharedMaterial = asphaltMaterial;
        }

        return road;
    }

    private void CreateLaneChild(Transform parent, List<Vector3> flowWaypoints, string laneName)
    {
        List<Vector3> offsetWaypoints = OffsetWaypointsRight(flowWaypoints, LaneOffset);

        GameObject laneObj = new GameObject(laneName);
        laneObj.transform.SetParent(parent);

        LaneAuthoring lane = laneObj.AddComponent<LaneAuthoring>();
        lane.waypoints = offsetWaypoints;
    }

    // ==========================================
    // LANE GEOMETRY MATH (X/Y Plane with Z elevation)
    // ==========================================

    /// <summary>
    /// Offsets every waypoint to the right of the flow direction using
    /// the 2D perpendicular: right = (dir.y, -dir.x, 0). Preserves Z.
    /// </summary>
    private static List<Vector3> OffsetWaypointsRight(List<Vector3> waypoints, float offset)
    {
        List<Vector3> result = new List<Vector3>(waypoints.Count);

        for (int i = 0; i < waypoints.Count; i++)
        {
            Vector3 dir;
            if (i < waypoints.Count - 1)
                dir = (waypoints[i + 1] - waypoints[i]).normalized;
            else
                dir = (waypoints[i] - waypoints[i - 1]).normalized;

            // 2D perpendicular right-hand vector (Z untouched)
            Vector3 right = new Vector3(dir.y, -dir.x, 0f);
            Vector3 offset3 = waypoints[i] + right * offset;
            offset3.z = waypoints[i].z;
            result.Add(offset3);
        }

        return result;
    }

    private static List<Vector3> ReverseWaypoints(List<Vector3> waypoints)
    {
        List<Vector3> reversed = new List<Vector3>(waypoints);
        reversed.Reverse();
        return reversed;
    }

    // ==========================================
    // DYNAMIC ROAD SPLITTING
    // ==========================================

    public Vertex SplitRoad(Road originalRoad, Vector3 splitPoint)
    {
        List<Vector3> pts = originalRoad.centerlineWaypoints;
        int splitIndex = 0;
        float minDist = float.MaxValue;

        // Find the exact curve segment to slice on the centerline
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

        // Build a RoadProperties from the original Road's data
        RoadProperties props = new RoadProperties
        {
            streetName = originalRoad.roadName,
            maxSpeed = (int)originalRoad.speedLimit,
            isOneWay = originalRoad.isOneWay
        };

        // 2. Spawn the two new Roads (with their own LaneAuthoring children)
        Road road1 = CreateRoad(originalRoad.vertexA, newVertex, part1, props, "_PartA");
        Road road2 = CreateRoad(newVertex, originalRoad.vertexB, part2, props, "_PartB");

        // 3. Destroy the original unbroken road
        Destroy(originalRoad.gameObject);

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