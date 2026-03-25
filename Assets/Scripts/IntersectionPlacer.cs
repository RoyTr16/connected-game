using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class IntersectionPlacer
{
    // A helper struct to keep track of angles
    private struct AngleData
    {
        public int Index;       // Original index of the road or socket
        public float Angle;     // Its angle in degrees (0 to 360)
        public Vector3 Forward; // The actual 3D direction
    }

    /// <summary>
    /// Rotates a prefab to best match the incoming road directions,
    /// and returns the matched sockets for each road.
    /// </summary>
    public static void AlignPrefabToRoads(IntersectionNode prefabNode, List<Vector3> incomingRoadDirections)
    {
        List<RoadSocket> sockets = prefabNode.GetSockets();

        // Safety check: Make sure we have the same number of roads and sockets
        if (sockets.Count != incomingRoadDirections.Count)
        {
            Debug.LogWarning("Mismatch between roads and sockets!");
            return;
        }

        int count = sockets.Count;

        // 1. Calculate angles and sort both lists CLOCKWISE
        List<AngleData> roadData = new List<AngleData>();
        for (int i = 0; i < count; i++)
        {
            roadData.Add(new AngleData {
                Index = i,
                Forward = incomingRoadDirections[i],
                Angle = NormalizeAngle(Vector3.SignedAngle(Vector3.forward, incomingRoadDirections[i], Vector3.up))
            });
        }

        List<AngleData> socketData = new List<AngleData>();
        for (int i = 0; i < count; i++)
        {
            socketData.Add(new AngleData {
                Index = i,
                Forward = sockets[i].Forward,
                Angle = NormalizeAngle(Vector3.SignedAngle(Vector3.forward, sockets[i].Forward, Vector3.up))
            });
        }

        roadData = roadData.OrderBy(r => r.Angle).ToList();
        socketData = socketData.OrderBy(s => s.Angle).ToList();

        // 2. Test the possible circular shifts to find the minimum error
        float bestError = float.MaxValue;
        float bestRotationOffset = 0f;

        for (int shift = 0; shift < count; shift++)
        {
            float totalAngleOffset = 0f;
            float maxVariance = 0f;

            // Calculate the required rotation for this specific mapping
            float[] angleDifferences = new float[count];
            for (int i = 0; i < count; i++)
            {
                int socketIdx = (i + shift) % count;
                // How much do we need to rotate the socket to hit the road?
                float diff = Mathf.DeltaAngle(socketData[socketIdx].Angle, roadData[i].Angle);
                angleDifferences[i] = diff;
                totalAngleOffset += diff;
            }

            // The optimal rotation for the prefab is the AVERAGE of these differences
            float averageOffset = totalAngleOffset / count;

            // Calculate how "wrong" this layout is (the variance from the average)
            for (int i = 0; i < count; i++)
            {
                float variance = Mathf.Abs(angleDifferences[i] - averageOffset);
                maxVariance += variance;
            }

            // If this is the best fit we've seen, save it!
            if (maxVariance < bestError)
            {
                bestError = maxVariance;
                bestRotationOffset = averageOffset;
            }
        }

        // 3. Apply the winning rotation to the Prefab!
        // We rotate the entire root object on the Y axis
        prefabNode.transform.Rotate(0, bestRotationOffset, 0, Space.World);
    }

    // Helper to keep angles between 0 and 360
    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0) angle += 360f;
        return angle;
    }
}