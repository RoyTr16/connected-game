# Architecture Blueprint: Massive Scale Traffic Simulation

## 1. The Objective
To simulate thousands of individual agents (civilian cars and transit vehicles) navigating a dynamically generated, player-modified spatial graph of real-world roads. The simulation must support individual tracking and dynamic rerouting without causing CPU bottlenecks or frame drops.

## 2. The Bottleneck (Why we avoid Object-Oriented Design)
The traditional Unity approach of creating a Prefab with a `MonoBehaviour` script (e.g., `CarMovement.cs`) running in `Update()` is fundamentally incompatible with city-scale simulations.
* **Memory Fragmentation:** 10,000 individual GameObjects are scattered randomly across the computer's RAM, causing massive CPU cache misses.
* **Single-Threaded Limits:** Unity runs standard `Update()` calls on a single core, leaving the rest of the CPU idle while the Main Thread chokes.
* **Warp Divergence:** Pushing the logic to the GPU (Compute Shaders) fails because complex state machines (yielding, parking, boarding) cause branching logic, which GPUs cannot process efficiently.

## 3. The Architecture: Data-Oriented Design (DOD)
To achieve *Cities: Skylines* level performance, the simulation will completely separate the **Data** from the **Visuals** using a hybrid DOTS (Data-Oriented Technology Stack) approach.

### A. The Logic (CPU / Unity Job System)
* **Structs, not Objects:** Vehicles will exist in memory as tightly packed structs (`CarData`) containing pure math: `Vector3 position`, `int currentEdgeIndex`, `float currentSpeed`, `int state`.
* **Native Arrays:** All vehicles will be stored in a contiguous `NativeArray`.
* **Multithreading:** Logic is pushed to Unity's **C# Job System**. The system will automatically slice the array of cars and distribute the calculations across every available CPU core simultaneously.
* **The Burst Compiler:** The C# Job code will be compiled into highly optimized, machine-level assembly code.

### B. The Visuals (GPU Instancing)
* **No GameObjects:** We will not instantiate 10,000 car models.
* **DrawMeshInstanced:** A single system will read the `NativeArray` of positions and send one command to the GPU: *"Draw this one car mesh 10,000 times at these specific coordinates."* This requires near-zero CPU overhead.

## 4. Traffic Classes
The simulation handles two distinct types of agents to balance performance and gameplay depth.

### Class A: Civilians ("Dumb" Flow)
* **Purpose:** Visual filler, dynamic obstacles, and traffic jam generation.
* **Routing:** "Wander Logic." Civilian cars do not run complex A* paths to specific destinations. At intersections, they probabilistically select a valid forward edge.
* **Spacing:** Governed by a lightweight Car-Following Model (e.g., Intelligent Driver Model). They adjust speed based purely on the distance to the car directly in front of them.

### Class B: Transit Fleet ("Smart" Agents)
* **Purpose:** The core gameplay pieces (Buses, Trams) managed by the player.
* **Routing:** Rigid adherence to the arrays of `Edges` defined by the player's transit lines.
* **Logic:** Requires a strict State Machine (`Driving`, `Yielding`, `Boarding Passengers`, `Waiting for Schedule`).

## 5. The Pathfinding Paradigm
Pathfinding is the most expensive operation in the game. It will be strictly decoupled from the frame-by-frame movement logic.
* **Run Once:** A* is only executed when an agent spawns or when their specific route is physically altered by the player.
* **The Blueprint:** The pathfinder returns an array of `Edge` IDs.
* **Blind Execution:** The agent is handed the array. Its only active brainpower is moving from the start of its current edge to the end of it, then asking the array for the next edge.
* **Queued Updates:** If the road network changes, agents are queued up and recalculated in batches over several frames to prevent stuttering.

## 6. Implementation Roadmap
To ensure mathematical accuracy before dealing with complex memory pointers, development will follow a three-phase stepping stone approach:

* **Phase 1: The Prototype.** Build the Data-Oriented structs (`CarData`) and a centralized `TrafficManager`. Temporarily use standard Unity GameObjects to visually verify the math and edge-following logic.
* **Phase 2: The Rules.** Implement intersection yielding, one-way enforcement, and the spacing logic to prevent cars from clipping through each other.
* **Phase 3: The Optimization.** Delete the GameObjects, transition the `TrafficManager` math into the C# Job System, and implement `Graphics.DrawMeshInstanced` for rendering.