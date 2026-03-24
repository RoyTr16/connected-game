using UnityEngine;
using System.Collections.Generic;

// This forces Unity to automatically add these components to the prefab
[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public class Vertex : MonoBehaviour
{
    [Header("Graph Data")]
    public List<Road> connectedRoads = new List<Road>();

    // We can toggle this when the player drops a bus stop here
    public bool isTransitStop = false;

    [Header("Visuals")]
    public Color defaultColor = Color.white;
    public Color selectedColor = Color.yellow;
    public Color transitStopColor = Color.green;

    private SpriteRenderer _spriteRenderer;
    public bool isSelected { get; private set; } = false;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _spriteRenderer.sortingOrder = 2; // NEW: Forces vertices to render ABOVE the roads
        UpdateVisuals();
    }

    // --- Graph Logic ---

    public void AddRoad(Road road)
    {
        if (!connectedRoads.Contains(road))
        {
            connectedRoads.Add(road);
        }
    }

    public void RemoveRoad(Road road)
    {
        if (connectedRoads.Contains(road))
        {
            connectedRoads.Remove(road);
        }
    }

    // --- Gameplay Logic ---

    public void ToggleSelection()
    {
        isSelected = !isSelected;
        UpdateVisuals();
    }

    public void SetAsTransitStop(bool status)
    {
        isTransitStop = status;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_spriteRenderer == null) return;

        if (isSelected) _spriteRenderer.color = selectedColor;
        else if (isTransitStop) _spriteRenderer.color = transitStopColor;
        else _spriteRenderer.color = defaultColor;
    }

    // Draws red debug lines in the Unity Editor to prove the graph is connected
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        foreach (Road road in connectedRoads)
        {
            if (road == null) continue;
            Vertex opposite = (road.vertexA == this) ? road.vertexB : road.vertexA;
            if (opposite != null)
            {
                Gizmos.DrawLine(transform.position, opposite.transform.position);
            }
        }
    }
}