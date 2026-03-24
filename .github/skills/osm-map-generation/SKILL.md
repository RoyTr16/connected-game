---
name: osm-map-generation
description: Strict guidelines for parsing OpenStreetMap (OSM) data and generating the Authoring Hierarchy (Roads and parallel Lanes).
---

# OSM Map Generation & Routing Topology

## 1. The Authoring Hierarchy
- **Road (Parent):** Represents the high-level OSM way (e.g., "Weizmann Street"). It contains meta-data like the road name, total lane count, and speed limit. It does NOT contain routing logic.
- **LaneAuthoring (Child):** Represents a single, unidirectional mathematical spline. Every road must mathematically generate its own parallel lanes based on OSM tags.

## 2. Parsing OSM Tags (The Generation Rules)
- **One-Way Streets (`oneway=yes`):** Generate `N` parallel `LaneAuthoring` children facing the direction of the OSM way nodes.
- **Two-Way Streets:** Must be split entirely. Generate `N/2` lanes facing forward, and `N/2` lanes with their waypoint arrays mathematically reversed.
- **Lane Offsets:** Lanes must be physically offset from the OSM centerline using a 2D perpendicular vector (Cross Product equivalent in 2D: `Right = (Dir.y, -Dir.x)`). Standard lane width is 3.0 meters (offset by 1.5m, 4.5m, etc., depending on lane index).
- **Intersection Nodes:** When an OSM node is shared by multiple ways, it represents an intersection. The generator must ensure Lane splines stop short of the absolute center to allow for Bezier curve generation later.

## 3. Data Integrity
- Do not use string parsing or `GetComponentInChildren` at runtime. All string manipulation and hierarchy parsing must happen strictly during Editor time or the Initialization/Baking phase.