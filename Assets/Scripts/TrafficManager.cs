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
    private NativeArray<int> laneTailCarIndices;
    // private GraphFlattener _flattener;

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
        // _flattener = new GraphFlattener();
        List<OsmTurnRestriction> restrictions = MapGenerator.Instance != null
            ? MapGenerator.Instance.turnRestrictions
            : new List<OsmTurnRestriction>();

        // Pass visual settings for connection meshes
        if (MapGenerator.Instance != null)
        {
            // _flattener.asphaltMaterial = MapGenerator.Instance.AsphaltMaterial;
            // _flattener.laneWidth = MapGenerator.Instance.laneWidth;
        }

        // _flattener.FlattenGraph(allRoads, restrictions);

        // int totalLanes = _flattener.nativeLanes.Length;
        // if (totalLanes == 0)
        // {
        //     Debug.LogError("No lanes found after flattening!");
        //     return;
        // }

        // 3. Allocate Memory for the cars (Allocator.Persistent means it lives forever)
        _cars = new NativeArray<CarData>(initialCarCount, Allocator.Persistent);
        _spatialData = new NativeArray<CarSpatialData>(initialCarCount, Allocator.Persistent);

        // Properly allocating the lookahead array based on total lanes!
        // laneTailCarIndices = new NativeArray<int>(totalLanes, Allocator.Persistent);

        _carVisuals = new Transform[initialCarCount];

        // 4. Spawn the traffic data
        for (int i = 0; i < initialCarCount; i++)
        {
            // Drop them on a random lane
            // int randomLane = UnityEngine.Random.Range(0, totalLanes);
            // float maxSpeed = _flattener.nativeLanes[randomLane].speedLimit;

            // CarData newCar = new CarData
            // {
            //     currentLaneIndex = randomLane,
            //     distanceAlongLane = 0f,
            //     currentSpeed = maxSpeed * 0.3f,
            //     maxSpeed = maxSpeed,
            //     state = 0,
            //     randomSeed = (uint)UnityEngine.Random.Range(1, 1000000),
            //     upcomingConnectionIndex = -1
            // };
            // _cars[i] = newCar;

            // Spawn the temporary visual representation
            GameObject visual = Instantiate(carVisualPrefab, transform);
            visual.name = $"Car_{i}";
            _carVisuals[i] = visual.transform;
        }
    }

    private void Update()
    {
        if (!_cars.IsCreated) return;

        // --- 1. SPATIAL PARTITIONING & SORT ---
        MapSpatialDataJob mapJob = new MapSpatialDataJob
        {
            cars = _cars,
            spatialData = _spatialData
        };
        JobHandle mapHandle = mapJob.Schedule(_cars.Length, 64);

        // We MUST complete this here so the main thread can sort the array
        mapHandle.Complete();
        _spatialData.Sort();

        // --- 2. THE TAIL FINDER ---
        FindLaneTailsJob tailJob = new FindLaneTailsJob
        {
            spatialData = _spatialData,
            laneTailCarIndices = laneTailCarIndices // Fed perfectly
        };
        // Schedule it (no dependency needed here since we just Completed the mapHandle above)
        JobHandle tailHandle = tailJob.Schedule();

        // --- 3. THE MOVEMENT & LOGIC ---
        // MoveTrafficJob moveJob = new MoveTrafficJob
        // {
        //     cars = _cars,
        //     spatialData = _spatialData,
        //     lanes = _flattener.nativeLanes,
        //     laneWaypoints = _flattener.laneWaypoints,
        //     connections = _flattener.nativeConnections,
        //     laneTailCarIndices = laneTailCarIndices, // Fed perfectly
        //     deltaTime = Time.deltaTime
        // };

        // Schedule the move job, telling it to wait for the tail finder to finish
        // JobHandle moveHandle = moveJob.Schedule(_cars.Length, 64, tailHandle);
        // moveHandle.Complete();

        // --- 4. VISUALS ---
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
        if (_cars.IsCreated) _cars.Dispose();
        // if (_flattener != null) _flattener.Dispose();
        if (_spatialData.IsCreated) _spatialData.Dispose();
        if (laneTailCarIndices.IsCreated) laneTailCarIndices.Dispose(); // Memory leak plugged!
    }
}