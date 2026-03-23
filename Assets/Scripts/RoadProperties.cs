using UnityEngine;
using Newtonsoft.Json.Linq;

// Enum to quickly categorize roads for rendering and AI pathfinding
public enum HighwayType
{
    Motorway,       // Highways
    Trunk,          // Major expressways
    Primary,        // Main city arteries (e.g., Weizmann St)
    Secondary,      // Standard roads
    Tertiary,       // Smaller neighborhood connectors
    Residential,    // Quiet streets
    Pedestrian,     // Walking paths only
    Unclassified    // Fallback
}

[System.Serializable]
public class RoadProperties
{
    public string streetName;
    public string streetNameEnglish;
    public HighwayType type;
    public bool isOneWay;
    public bool isReversedOneWay; // For when OSM says oneway="-1"
    public int maxSpeed;
    public bool isRoundabout;

    // A static factory method to parse the messy JSON into our clean object
    public static RoadProperties ParseFromGeoJSON(JToken propertiesToken)
    {
        RoadProperties props = new RoadProperties();

        // 1. Parse Names
        props.streetName = propertiesToken["name"]?.ToString() ?? "Unnamed Road";
        props.streetNameEnglish = propertiesToken["name:en"]?.ToString() ?? props.streetName;

        // 2. Parse Highway Type
        string highwayTag = propertiesToken["highway"]?.ToString().ToLower() ?? "unclassified";
        props.type = ParseHighwayType(highwayTag);

        // 3. Parse One-Way Data
        string oneWayTag = propertiesToken["oneway"]?.ToString().ToLower();
        if (oneWayTag == "yes" || oneWayTag == "true" || oneWayTag == "1")
        {
            props.isOneWay = true;
            props.isReversedOneWay = false;
        }
        else if (oneWayTag == "-1" || oneWayTag == "reverse")
        {
            props.isOneWay = true;
            props.isReversedOneWay = true; // The AI needs to drive backwards down the coordinate list
        }
        else
        {
            props.isOneWay = false;
        }

        // 4. Parse Roundabouts
        string junctionTag = propertiesToken["junction"]?.ToString().ToLower();
        if (junctionTag == "roundabout")
        {
            props.isRoundabout = true;
            // Roundabouts are strictly one-way by definition
            props.isOneWay = true;
        }

        // 5. Parse Speed Limits (OSM maxspeed is usually in km/h)
        string speedTag = propertiesToken["maxspeed"]?.ToString();
        props.maxSpeed = ParseSpeedLimit(speedTag, props.type);

        return props;
    }

    // --- Helper Methods ---

    private static HighwayType ParseHighwayType(string tag)
    {
        switch (tag)
        {
            case "motorway": return HighwayType.Motorway;
            case "trunk": return HighwayType.Trunk;
            case "primary": return HighwayType.Primary;
            case "secondary": return HighwayType.Secondary;
            case "tertiary": return HighwayType.Tertiary;
            case "residential": return HighwayType.Residential;
            case "pedestrian":
            case "footway":
            case "path": return HighwayType.Pedestrian;
            default: return HighwayType.Unclassified;
        }
    }

    private static int ParseSpeedLimit(string speedTag, HighwayType type)
    {
        if (!string.IsNullOrEmpty(speedTag))
        {
            // Strip out text like " mph" or " km/h" just in case, taking only the numbers
            string numericString = System.Text.RegularExpressions.Regex.Match(speedTag, @"\d+").Value;
            if (int.TryParse(numericString, out int speed))
            {
                return speed;
            }
        }

        // If OSM didn't provide a speed limit, we assign a logical default based on the road type
        switch (type)
        {
            case HighwayType.Motorway: return 110;
            case HighwayType.Trunk: return 90;
            case HighwayType.Primary: return 70;
            case HighwayType.Secondary: return 50;
            case HighwayType.Tertiary: return 50;
            case HighwayType.Residential: return 30;
            case HighwayType.Pedestrian: return 5;
            default: return 50;
        }
    }
}