using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways] // This lets us see the mesh in the Editor without hitting Play!
public class BezierRoadTest : MonoBehaviour
{
    public IntersectionNode nodeA;
    public IntersectionNode nodeB;
    public Material asphaltMaterial;

    [Range(2, 50)]
    public int edgeResolution = 15; // How many segments make up the curve

    private MeshFilter mf;
    private MeshRenderer mr;
    private Mesh generatedMesh;

    void Update()
    {
        // Only run if we have both nodes assigned
        if (nodeA == null || nodeB == null) return;

        List<RoadSocket> socketsA = nodeA.GetSockets();
        List<RoadSocket> socketsB = nodeB.GetSockets();

        if (socketsA.Count == 0 || socketsB.Count == 0) return;

        // For this test, just grab the first socket from each intersection
        RoadSocket startSocket = socketsA[0];
        RoadSocket endSocket = socketsB[0];

        GenerateRoadMesh(startSocket, endSocket);
    }

    private void GenerateRoadMesh(RoadSocket start, RoadSocket end)
    {
        if (mf == null)
        {
            if (!gameObject.TryGetComponent(out mf))
                mf = gameObject.AddComponent<MeshFilter>();
        }

        if (mr == null)
        {
            if (!gameObject.TryGetComponent(out mr))
                mr = gameObject.AddComponent<MeshRenderer>();
        }

        mr.sharedMaterial = asphaltMaterial;

        // 1. Memory Leak Fix: Create the mesh ONCE and reuse it
        if (generatedMesh == null)
        {
            generatedMesh = new Mesh { name = "BezierRoadMesh" };
            mf.sharedMesh = generatedMesh; // MUST use sharedMesh in the Editor!
        }

        // Clear the old data instead of making a new object
        generatedMesh.Clear();

        // 2. Setup the 4 Bezier Points
        float distance = Vector3.Distance(start.Position, end.Position);
        float tangentLength = distance * 0.4f;

        Vector3 p0 = start.Position;
        Vector3 p1 = start.Position + (start.Forward * tangentLength);
        Vector3 p2 = end.Position + (end.Forward * tangentLength);
        Vector3 p3 = end.Position;

        // 3. Prepare Mesh Data
        int numVertices = (edgeResolution + 1) * 2;
        Vector3[] vertices = new Vector3[numVertices];
        Vector2[] uvs = new Vector2[numVertices];
        int[] triangles = new int[edgeResolution * 6];

        float accumulatedDistance = 0f;
        Vector3 previousCenter = p0;

        // 4. Walk along the curve and extrude the road
        for (int i = 0; i <= edgeResolution; i++)
        {
            float t = i / (float)edgeResolution;
            Vector3 center = CalculateBezierPoint(t, p0, p1, p2, p3);
            Vector3 forward = CalculateBezierTangent(t, p0, p1, p2, p3).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            accumulatedDistance += Vector3.Distance(previousCenter, center);
            previousCenter = center;

            float halfWidth = start.Width / 2f;
            vertices[i * 2] = center - (right * halfWidth);
            vertices[i * 2 + 1] = center + (right * halfWidth);

            uvs[i * 2] = new Vector2(0f, accumulatedDistance / 10f);
            uvs[i * 2 + 1] = new Vector2(1f, accumulatedDistance / 10f);
        }

        // 5. Stitch the triangles together
        int vertIndex = 0;
        int triIndex = 0;
        for (int i = 0; i < edgeResolution; i++)
        {
            triangles[triIndex] = vertIndex;
            triangles[triIndex + 1] = vertIndex + 2;
            triangles[triIndex + 2] = vertIndex + 1;

            triangles[triIndex + 3] = vertIndex + 1;
            triangles[triIndex + 4] = vertIndex + 2;
            triangles[triIndex + 5] = vertIndex + 3;

            vertIndex += 2;
            triIndex += 6;
        }

        // 6. Assign the new data to our single, cached mesh object
        generatedMesh.vertices = vertices;
        generatedMesh.uv = uvs;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateNormals();
    }

    // Standard Cubic Bezier Math
    Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        return uuu * p0 + 3 * uu * t * p1 + 3 * u * tt * p2 + ttt * p3;
    }

    // Derivative of the Bezier (gives us the forward vector at any point)
    Vector3 CalculateBezierTangent(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1 - t;
        return 3 * u * u * (p1 - p0) + 6 * u * t * (p2 - p1) + 3 * t * t * (p3 - p2);
    }
}