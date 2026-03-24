---
name: dots-traffic-architecture
description: Strict guidelines for the Data-Oriented Technology Stack (DOTS) and Authoring vs. Runtime split for the Kfar Saba traffic simulation.
---

# DOTS Traffic Architecture & Workflow

## 1. The Authoring vs. Runtime Split (Core Rule)
- **Authoring (Editor-Time):** Standard Unity `GameObjects` and `MonoBehaviours` are strictly for human-readable map generation and editor configuration. The hierarchy consists of `Road` parents and parallel, one-way `LaneAuthoring` children.
- **Baking (Initialization):** A `GraphFlattener` system parses the Authoring hierarchy on startup and converts all splines, rules, and distances into flat `NativeArray<T>` structs.
- **Simulation (Runtime):** Once baked, the multithreaded Jobs completely ignore GameObjects. The simulation runs entirely on the flat `NativeArrays`. Do not use `Update()` loops on individual car GameObjects for logic.

## 2. Memory & Graph Structure (Lane-Based)
- **Strict 1D Lanes:** The routing graph is purely Lane-based. There are no two-way "Edges" in the mathematical simulation. Every `LaneStruct` is a one-way flow of traffic.
- **No Runtime Cross-Products:** Cars do not calculate their lane offsets during the multithreaded job. The 1.5-meter offset is pre-calculated during the `GraphFlattener` baking phase.
- **Memory Allocation:** All `NativeArray` structures must be allocated with `Allocator.Persistent` during initialization and explicitly disposed of in `OnDestroy()`.

## 3. The Job System Rules (Burst Compiler Constraints)
- **Execution:** Movement and logic are handled by an `IJobParallelFor` compiled with `[BurstCompile]`.
- **Prohibited Code:** Inside the Job, you MUST NOT use:
  - Classes or reference types (only `structs`).
  - Unity's `Physics.Raycast` or colliders.
  - `UnityEngine.Random` (use `Unity.Mathematics.Random` with a seeded `uint`).
  - `Vector3` or `Quaternion` (use `float3` and `quaternion` from `Unity.Mathematics`).

## 4. Traffic Physics & Collision Avoidance
- **1D Spatial Partitioning:** To avoid O(N^2) checks, cars are mapped to a `CarSpatialData` struct every frame and sorted natively by `laneIndex` and `distanceAlongLane` (descending).
- **O(1) Lookups:** A car finds the vehicle directly ahead by checking the `[i - 1]` index of the sorted spatial array.
- **Intelligent Driver Model (IDM):** Cars use the IDM formula for smooth acceleration and deceleration. They do not instantly snap to speeds.
- **Look-Ahead Braking:** The IDM factors in upcoming sharp turns (Bezier curves) by dynamically lowering the desired speed limit ($v0$) as the car approaches the turn trigger.