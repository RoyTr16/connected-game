using System.Collections.Generic;
using UnityEngine;

public class CitySpawner : MonoBehaviour
{
    [Header("Hardware Components")]
    [Tooltip("Drag your Intersection_3Way_Root prefab here")]
    public IntersectionNode prefab3Way;

    [Header("Controls")]
    [Tooltip("Check this box in the Inspector to trigger the generation")]
    public bool generateNow = false;

    void Update()
    {
        // A simple Editor button hack so we don't have to write custom UI code yet
        if (generateNow)
        {
            generateNow = false;
            SpawnIntersections();
        }
    }

    public void SpawnIntersections()
    {
        if (prefab3Way == null)
        {
            Debug.LogError("CitySpawner: Please assign the 3-Way Prefab!");
            return;
        }

        // 1. Find all parsed OSM vertices in the scene
        Vertex[] allVertices = FindObjectsByType<Vertex>(FindObjectsInactive.Exclude);

        // Create a clean folder to hold our instances
        GameObject container = new GameObject("Intersection_Instances");
        int spawnedCount = 0;

        foreach (Vertex v in allVertices)
        {
            // Safety check: We only support 3-way intersections right now!
            if (v.connectedRoads != null && v.connectedRoads.Count == 3)
            {
                // 2. Instantiate the prefab exactly at the Vertex's GPS coordinate
                IntersectionNode instance = Instantiate(prefab3Way, v.transform.position, Quaternion.identity, container.transform);
                instance.gameObject.name = $"Intersection_3Way_{v.gameObject.name}";

                // 3. Calculate the incoming vectors from the connected OSM roads
                List<Vector3> incomingDirections = new List<Vector3>();

                foreach (Road road in v.connectedRoads)
                {
                    // Find out where this specific road is coming from
                    Vertex oppositeNode = road.GetOppositeNode(v);

                    Vector3 direction = (oppositeNode.transform.position - v.transform.position).normalized;

                    // Flatten the Y-axis to ensure our math stays perfectly 2D, even if your map has hills
                    direction.y = 0;
                    incomingDirections.Add(direction.normalized);
                }

                // 4. Run our Alignment Optimization algorithm!
                IntersectionPlacer.AlignPrefabToRoads(instance, incomingDirections);
                spawnedCount++;
            }
        }

        Debug.Log($"Success! Spawned and aligned {spawnedCount} 3-Way Intersections.");
    }
}