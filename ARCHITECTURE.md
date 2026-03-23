# "Connected" - Map & Transit Architecture Specification

## 1. Architectural Goals
The map system in "Connected" operates on a **Directed Spatial Graph**. It is designed to move away from static, visual-only lines and toward a fully interactive, mathematically sound simulation grid.

This architecture supports three primary gameplay pillars:
1. **Manual Tactile Routing:** Players can drag transit lines that physically "snap" to the road network.
2. **Dynamic City Editing:** Players can build new roads, bulldoze existing ones, and place transit stops anywhere (including the middle of a block).
3. **Agent-Based Traffic Simulation:** Independent AI vehicles (cars, buses) can calculate the fastest routes, obey speed limits, and respect one-way streets and roundabouts.

---

## 2. Core Data Structures

To separate the visual game from the mathematical simulation, the road network is broken down into four distinct components.

### A. The `Vertex` (Nodes)
A `Vertex` is an anchor point in the spatial graph.
* **Role:** Represents intersections, dead-ends, and player-placed transit stops.
* **Data:** Holds its exact `Vector3` world position and a list of connected `Edge` objects.
* **Physics:** Uses a `CircleCollider2D` (if interactable by the player).

### B. The `Edge` (Physical Street Segments)
An `Edge` is the physical piece of asphalt connecting exactly two `Vertices`.
* **Role:** The visual and physical representation of the road block. This is what the player interacts with to draw routes or bulldoze.
* **Data:** Contains a list of `Vector3` waypoints so the visual line curves accurately between the two vertices.
* **Physics:** Uses an `EdgeCollider2D` that perfectly traces the curve of the waypoints, allowing mouse raycasts to "snap" to the street.

### C. The `Lane` (Logical Traffic Flow)
An invisible mathematical path attached to an `Edge`, used exclusively by the AI pathfinding system.
* **Role:** Dictates the direction of travel and traffic laws. Vehicles do not drive on "Edges"; they drive in "Lanes."
* **Data:** * `startVertex` and `endVertex` (defines direction).
  * `weight` (Calculated by physical distance and speed limit).
* **Generation:** A standard two-way `Edge` generates two `Lanes` (one in each direction). A one-way `Edge` generates only one `Lane`.

### D. `RoadProperties` (Metadata)
Data parsed directly from the OpenStreetMap GeoJSON tags.
* **`highwayType`**: Categorizes the road (motorway, residential, pedestrian).
* **`isOneWay`**: Boolean determining if the street allows bidirectional traffic.
* **`maxSpeed`**: Used to calculate the true $f(n)$ travel time for the A* pathfinder.
* **`isRoundabout`**: Enforces strict one-way, counter-clockwise lane generation.

---

## 3. Gameplay Workflows & Mechanics

### Workflow 1: Dynamic Edge Splitting (Placing a Stop)
The player does not have to place bus stops exclusively at intersections. They can place them anywhere along a street.
1. **Trigger:** Player clicks the middle of an existing `Edge` to build a station.
2. **Execution:** The engine calculates the exact coordinate on the `EdgeCollider2D`.
3. **Graph Update:** The original `Edge` is destroyed. Two new `Edges` are created: `Vertex A -> New Station` and `New Station -> Vertex B`.
4. **Result:** The new station is instantly integrated as a valid topological `Vertex` in the A* network.

### Workflow 2: Tactile Route Tracing
Transit lines are not automatically pathfound; they are manually traced by the player for a satisfying, puzzle-like experience.
1. **Start:** Player clicks a starting `Vertex` (Station).
2. **Drag:** Player drags the mouse. The Raycast hits adjacent `EdgeCollider2D` objects.
3. **Snap & Anchor:** As the mouse hovers over a valid connected `Edge`, the game visually highlights it. When the mouse reaches the `Vertex` at the end of that edge, the route is locked in.
4. **Validation:** The engine verifies that the sequence of `Edges` forms a continuous, unbroken chain in the graph.

### Workflow 3: City Editing (Bulldozing)
1. **Trigger:** Player selects the Bulldozer tool and clicks an `Edge`.
2. **Execution:** The `Edge` is destroyed (`Destroy(gameObject)`).
3. **Graph Update:** The `Lanes` associated with that edge are deleted. The two `Vertices` it connected remove the edge from their adjacency lists.
4. **AI Response:** Any traffic AI currently calculating a route will see the connection is severed and dynamically recalculate a detour.

---

## 4. Map Generation Pipeline (OSM Parsing)
To prevent "Phantom Edges" and topological errors from raw OSM data, the generation pipeline strictly follows these rules:
1. **Read Metadata First:** Parse `RoadProperties` to determine if a line string is one-way or a roundabout.
2. **Topological Merging:** Intersections are only created when two roads explicitly share the exact same OSM coordinate ID, *or* when a single road's property tags change (e.g., speed limit drops). Distance-guessing (`Vector3.Distance`) is strictly prohibited for intersection creation to prevent overpass snapping.
3. **Instantiation:** Generate the `Vertices` first, then span the `Edges` between them, injecting the curved visual waypoints into the Edge's `LineRenderer`.
4. **Lane Generation:** Overlay the invisible `Lanes` based on the parsed `RoadProperties`.