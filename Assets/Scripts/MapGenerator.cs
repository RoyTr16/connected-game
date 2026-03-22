using UnityEngine;
using Newtonsoft.Json.Linq;
using System.IO;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Data")]
    [Tooltip("Drag your *.geojson file here")]
    public TextAsset geoJsonFile;

    [Header("Rendering Settings")]
    public Material lineMaterial;
    public float roadWidth = 0.5f;

    // Scale factor to convert Lat/Lon degrees into Unity meters.
    // 1 degree of latitude is roughly 111,000 meters.
    private float mapScale = 111000f;

    // We will use the very first coordinate we find as the (0,0) center of our Unity world.
    private Vector2 originLonLat;
    private bool isOriginSet = false;

    void Start()
    {
        if (geoJsonFile != null)
        {
            GenerateRoads();
        }
        else
        {
            Debug.LogError("No GeoJSON file assigned to the MapGenerator!");
        }
    }

    void GenerateRoads()
    {
        // Parse the entire text file into a JObject
        JObject mapData = JObject.Parse(geoJsonFile.text);
        JArray features = (JArray)mapData["features"];

        int roadCount = 0;

        // Loop through every feature (road) in the file
        foreach (JToken feature in features)
        {
            JToken geometry = feature["geometry"];

            // We only want to draw continuous lines (roads)
            if (geometry["type"].ToString() == "LineString")
            {
                JArray coordinates = (JArray)geometry["coordinates"];
                DrawRoad(coordinates, $"Road_{roadCount}");
                roadCount++;
            }
        }

        Debug.Log($"Successfully generated {roadCount} roads.");
    }

    void DrawRoad(JArray coordinates, string roadName)
    {
        // 1. Create a new empty GameObject for this road
        GameObject roadObj = new GameObject(roadName);
        roadObj.transform.SetParent(this.transform); // Group it under the MapGenerator object

        // 2. Add and configure the LineRenderer
        LineRenderer lr = roadObj.AddComponent<LineRenderer>();
        lr.positionCount = coordinates.Count;
        lr.startWidth = roadWidth;
        lr.endWidth = roadWidth;
        lr.useWorldSpace = false;
        lr.numCapVertices = 4; // Makes the ends of the roads rounded
        lr.numCornerVertices = 4; // Makes the bends in the roads rounded

        if (lineMaterial != null) lr.material = lineMaterial;

        // 3. Convert Lat/Lon to Unity X/Y and set the line points
        for (int i = 0; i < coordinates.Count; i++)
        {
            float lon = (float)coordinates[i][0];
            float lat = (float)coordinates[i][1];

            // Lock the first point as the absolute center of our game world
            if (!isOriginSet)
            {
                originLonLat = new Vector2(lon, lat);
                isOriginSet = true;
            }

            // Math: Calculate the difference from the origin and scale it up.
            // We multiply the X (Longitude) by Cosine of the Latitude to account for the earth's curvature.
            float x = (lon - originLonLat.x) * mapScale * Mathf.Cos(originLonLat.y * Mathf.Deg2Rad);
            float y = (lat - originLonLat.y) * mapScale;

            lr.SetPosition(i, new Vector3(x, y, 0f));
        }
    }
}