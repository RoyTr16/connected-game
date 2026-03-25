using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BezierRoadTest : MonoBehaviour
{
    public IntersectionNode nodeA;
    public IntersectionNode nodeB;
    public Material asphaltMaterial;

    [Range(2, 50)]
    public int edgeResolution = 15;

    private MeshFilter mf;
    private MeshRenderer mr;
    private Mesh generatedMesh;

    private void OnEnable()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
    }

    // --- THE CRITICAL FIX IS HERE ---
    private void OnDisable()
    {
        // 1. Force the MeshFilter to let go of the mesh BEFORE we destroy it.
        // This stops the Inspector from trying to read a dead object during recompile.
        if (mf != null)
        {
            mf.sharedMesh = null;
        }

        // 2. Safely destroy the mesh
        if (generatedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);

            generatedMesh = null;
        }
    }

    void Update()
    {
        if (nodeA == null || nodeB == null) return;

        List<RoadSocket> socketsA = nodeA.GetSockets();
        List<RoadSocket> socketsB = nodeB.GetSockets();

        if (socketsA.Count == 0 || socketsB.Count == 0) return;

        GenerateRoadMesh(socketsA[0], socketsB[0]);
    }

    private void GenerateRoadMesh(RoadSocket start, RoadSocket end)
    {
        if (mr.sharedMaterial != asphaltMaterial)
            mr.sharedMaterial = asphaltMaterial;

        if (generatedMesh == null)
        {
            generatedMesh = new Mesh { name = "BezierRoadMesh" };
            generatedMesh.hideFlags = HideFlags.DontSave;
        }

        generatedMesh.Clear();

        float distance = Vector3.Distance(start.Position, end.Position);
        float tangentLength = distance * 0.4f;

        Vector3 p0 = start.Position;
        Vector3 p1 = start.Position + (start.Forward * tangentLength);
        Vector3 p2 = end.Position + (end.Forward * tangentLength);
        Vector3 p3 = end.Position;

        int numVertices = (edgeResolution + 1) * 2;
        Vector3[] vertices = new Vector3[numVertices];
        Vector2[] uvs = new Vector2[numVertices];
        int[] triangles = new int[edgeResolution * 6];

        float accumulatedDistance = 0f;
        Vector3 previousCenter = p0;

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

        generatedMesh.vertices = vertices;
        generatedMesh.uv = uvs;
        generatedMesh.triangles = triangles;
        generatedMesh.RecalculateNormals();

        // Reassign the updated mesh
        if (mf.sharedMesh != generatedMesh)
        {
            mf.sharedMesh = generatedMesh;
        }
    }

    Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;
        return uuu * p0 + 3 * uu * t * p1 + 3 * u * tt * p2 + ttt * p3;
    }

    Vector3 CalculateBezierTangent(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1 - t;
        return 3 * u * u * (p1 - p0) + 6 * u * t * (p2 - p1) + 3 * t * t * (p3 - p2);
    }
}