using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Linq;

public class TrafficManager : MonoBehaviour
{

    public static TrafficManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    [Header("Simulation Settings")]
    public int initialCarCount = 1000;
    public GameObject carVisualPrefab; // We'll use a simple cube for now

    // The Raw Memory
    private NativeArray<CarData> _cars;
    private GraphFlattener _flattener;

    // Phase 1 Visuals (We will delete this entirely in Phase 3 when we use the GPU)
    private Transform[] _carVisuals;

    public void InitializeTraffic()
    {
        // 1. Grab all active roads in the city
        List<Edge> allEdges = FindObjectsByType<Edge>(FindObjectsInactive.Exclude).ToList();
        if (allEdges.Count == 0)
        {
            Debug.LogError("No roads found! Generate the map first.");
            return;
        }

        // 2. Flatten the Object-Oriented map into hardware-friendly Native memory
        _flattener = new GraphFlattener();
        _flattener.FlattenGraph(allEdges);

        // 3. Allocate Memory for the cars (Allocator.Persistent means it lives forever)
        _cars = new NativeArray<CarData>(initialCarCount, Allocator.Persistent);
        _carVisuals = new Transform[initialCarCount];

        // 4. Spawn the traffic data
        for (int i = 0; i < initialCarCount; i++)
        {
            // Drop them on a random road
            int randomEdge = UnityEngine.Random.Range(0, allEdges.Count);
            float maxSpeed = _flattener.nativeEdges[randomEdge].speedLimit;

            CarData newCar = new CarData
            {
                currentEdgeIndex = randomEdge,
                distanceAlongEdge = 0f,
                currentSpeed = maxSpeed * 0.3f,
                maxSpeed = maxSpeed,
                state = 0,

                drivingForward = true, // Start them off driving A->B
                randomSeed = (uint)UnityEngine.Random.Range(1, 1000000) // MUST be > 0
            };
            _cars[i] = newCar;

            // Spawn the temporary visual representation
            GameObject visual = Instantiate(carVisualPrefab, transform);
            visual.name = $"Car_{i}";
            _carVisuals[i] = visual.transform;
        }
    }

    private void Update()
    {
        if (!_cars.IsCreated) return;

        // Setup the multithreaded math job
        MoveTrafficJob moveJob = new MoveTrafficJob
        {
            cars = _cars,
            edges = _flattener.nativeEdges,
            centerlineWaypoints = _flattener.centerlineWaypoints, // RENAMED
            intersectionWaypoints = _flattener.intersectionWaypoints, // NEW
            connections = _flattener.nativeConnections,
            deltaTime = Time.deltaTime
        };

        // Schedule and run the multithreaded jobs
        JobHandle handle = moveJob.Schedule(_cars.Length, 64);
        handle.Complete();

        // Standard Main Thread visual update
        for (int i = 0; i < _cars.Length; i++)
        {
            _carVisuals[i].position = _cars[i].position;
            _carVisuals[i].rotation = _cars[i].rotation;
        }
    }

    private void OnDestroy()
    {
        // CRITICAL: C# Garbage Collection ignores NativeArrays.
        // If we don't dispose of these, the computer will leak memory until it crashes.
        if (_cars.IsCreated) _cars.Dispose();
        if (_flattener != null) _flattener.Dispose();
    }
}