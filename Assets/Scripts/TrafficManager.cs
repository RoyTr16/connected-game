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

    [Header("Debug Visualization")]
    public bool showDebugGizmos = false; // Default to false for performance

    [Header("Simulation Settings")]
    public int initialCarCount = 1000;
    public GameObject carVisualPrefab; // We'll use a simple cube for now

    // The Raw Memory
    private NativeArray<CarData> _cars;
    private NativeArray<CarSpatialData> _spatialData;
    private NativeArray<int> _laneTailCarIndices;
    private GraphFlattener _flattener;

    // Phase 1 Visuals (We will delete this entirely in Phase 3 when we use the GPU)
    private Transform[] _carVisuals;

    public void InitializeTraffic()
    {
        // 1. Grab all active roads in the city
        List<Road> allRoads = FindObjectsByType<Road>(FindObjectsInactive.Exclude).ToList();
        if (allRoads.Count == 0)
        {
            Debug.LogError("No roads found! Generate the map first.");
            return;
        }

        // 2. Flatten the Object-Oriented map into hardware-friendly Native memory
        _flattener = new GraphFlattener();
        List<OsmTurnRestriction> restrictions = MapGenerator.Instance != null
            ? MapGenerator.Instance.turnRestrictions
            : new List<OsmTurnRestriction>();
        _flattener.FlattenGraph(allRoads, restrictions);

        int totalLanes = _flattener.nativeLanes.Length;
        if (totalLanes == 0)
        {
            Debug.LogError("No lanes found after flattening!");
            return;
        }

        // 3. Allocate Memory for the cars (Allocator.Persistent means it lives forever)
        _cars = new NativeArray<CarData>(initialCarCount, Allocator.Persistent);
        _spatialData = new NativeArray<CarSpatialData>(initialCarCount, Allocator.Persistent);
        _laneTailCarIndices = new NativeArray<int>(totalLanes, Allocator.Persistent);
        _carVisuals = new Transform[initialCarCount];

        // 4. Spawn the traffic data
        for (int i = 0; i < initialCarCount; i++)
        {
            // Drop them on a random lane
            int randomLane = UnityEngine.Random.Range(0, totalLanes);
            float maxSpeed = _flattener.nativeLanes[randomLane].speedLimit;

            CarData newCar = new CarData
            {
                currentLaneIndex = randomLane,
                distanceAlongLane = 0f,
                currentSpeed = maxSpeed * 0.3f,
                maxSpeed = maxSpeed,
                state = 0,
                randomSeed = (uint)UnityEngine.Random.Range(1, 1000000),
                upcomingConnectionIndex = -1
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

        // --- 1. SPATIAL PARTITIONING (THE MAP & SORT) ---
        MapSpatialDataJob mapJob = new MapSpatialDataJob
        {
            cars = _cars,
            spatialData = _spatialData
        };
        JobHandle mapHandle = mapJob.Schedule(_cars.Length, 64);

        mapHandle.Complete();
        _spatialData.Sort();

        // --- 1b. FIND TAIL CARS PER LANE ---
        FindLaneTailsJob tailJob = new FindLaneTailsJob
        {
            laneTailCarIndices = _laneTailCarIndices,
            spatialData = _spatialData
        };
        tailJob.Schedule().Complete();

        // --- 2. THE MOVEMENT & LOGIC ---
        MoveTrafficJob moveJob = new MoveTrafficJob
        {
            cars = _cars,
            spatialData = _spatialData,
            lanes = _flattener.nativeLanes,
            laneWaypoints = _flattener.laneWaypoints,
            intersectionWaypoints = _flattener.intersectionWaypoints,
            connections = _flattener.nativeConnections,
            laneTailCarIndices = _laneTailCarIndices,
            deltaTime = Time.deltaTime
        };

        JobHandle moveHandle = moveJob.Schedule(_cars.Length, 64);
        moveHandle.Complete();

        // --- 3. VISUALS ---
        for (int i = 0; i < _cars.Length; i++)
        {
            Vector3 pos = _cars[i].position;
            pos.z -= 0.5f; // Lift cars above the road surface to prevent Z-fighting
            _carVisuals[i].position = pos;
            _carVisuals[i].rotation = _cars[i].rotation;
        }
    }

    private void OnDestroy()
    {
        // CRITICAL: C# Garbage Collection ignores NativeArrays.
        // If we don't dispose of these, the computer will leak memory until it crashes.
        if (_cars.IsCreated) _cars.Dispose();
        if (_flattener != null) _flattener.Dispose();
        if (_spatialData.IsCreated) _spatialData.Dispose();
        if (_laneTailCarIndices.IsCreated) _laneTailCarIndices.Dispose();
    }
}