using UnityEngine;

[System.Serializable]
public struct RoadSocket
{
    public Vector3 Position;
    public Vector3 Forward;
    public float Width;

    public RoadSocket(Vector3 position, Vector3 forward, float width)
    {
        Position = position;
        Forward = forward.normalized;
        Width = width;
    }
}