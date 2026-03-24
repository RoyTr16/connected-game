using UnityEngine;
using System.Collections.Generic;

public static class RoadMeshBuilder
{
    public static Mesh CreateRoadMesh(List<Vector3> waypoints, float width)
    {
        if (waypoints == null || waypoints.Count < 2)
            return new Mesh();

        int count = waypoints.Count;
        Vector3[] vertices = new Vector3[count * 2];
        Vector2[] uvs = new Vector2[count * 2];
        int[] triangles = new int[(count - 1) * 6];

        float halfWidth = width / 2f;
        float cumulativeDistance = 0f;

        for (int i = 0; i < count; i++)
        {
            // Calculate forward direction
            Vector3 forward;
            if (i < count - 1)
                forward = (waypoints[i + 1] - waypoints[i]).normalized;
            else
                forward = (waypoints[i] - waypoints[i - 1]).normalized;

            // Right vector perpendicular to forward and world up (-Z is up in our coordinate system)
            Vector3 right = Vector3.Cross(Vector3.back, forward).normalized;

            // If cross product is degenerate, fall back
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.Cross(Vector3.up, forward).normalized;

            vertices[i * 2] = waypoints[i] - right * halfWidth;
            vertices[i * 2 + 1] = waypoints[i] + right * halfWidth;

            // Track cumulative distance for UV tiling
            if (i > 0)
                cumulativeDistance += Vector3.Distance(waypoints[i], waypoints[i - 1]);

            uvs[i * 2] = new Vector2(cumulativeDistance, 0f);
            uvs[i * 2 + 1] = new Vector2(cumulativeDistance, 1f);
        }

        // Build triangles with clockwise winding (facing -Z, toward camera)
        for (int i = 0; i < count - 1; i++)
        {
            int bl = i * 2;       // bottom-left
            int br = i * 2 + 1;   // bottom-right
            int tl = (i + 1) * 2; // top-left
            int tr = (i + 1) * 2 + 1; // top-right

            int t = i * 6;
            // Triangle 1
            triangles[t] = bl;
            triangles[t + 1] = tl;
            triangles[t + 2] = br;
            // Triangle 2
            triangles[t + 3] = br;
            triangles[t + 4] = tl;
            triangles[t + 5] = tr;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }
}
