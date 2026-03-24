---
name: visual-debugging-gizmos
description: Mandates the use of Unity Gizmos to visually expose the internal memory state of the DOTS NativeArrays.
---

# Visual Debugging & System State Verification

## 1. Visualizing the Invisible
Because the simulation runs entirely inside flat `NativeArray<T>` structs processed by the Burst compiler, the Unity Inspector is blind to the runtime state of the traffic. All generated logic must include visual debuggers.

## 2. Mandatory Gizmo Implementations
When writing or updating the `TrafficManager` or `GraphFlattener`, you must write `OnDrawGizmos()` or `OnDrawGizmosSelected()` methods to draw the underlying math:
- **Lane Waypoints:** Draw spheres at `laneWaypoints` and lines connecting them to verify spline generation and offsets.
- **Intersection Curves:** Draw distinct colored lines (e.g., Cyan or Magenta) for the pre-baked Bezier curves stored in `intersectionWaypoints`.
- **Look-Ahead Braking:** When a car is approaching a turn, draw a debug line from the car to the `turnTriggerDistance` to verify the IDM is seeing the upcoming speed limit change.

## 3. Safe Memory Access
- **Job Safety:** Ensure `OnDrawGizmos` only accesses `NativeArrays` when the `JobHandle` has completed. Never attempt to draw Gizmos while the `MoveTrafficJob` is actively scheduled and running.
- **Visual Scale:** Use `Gizmos.DrawWireSphere` or `Gizmos.DrawLine` with appropriate scaling so data points are clearly visible from an orthographic top-down camera view.