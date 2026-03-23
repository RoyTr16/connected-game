using UnityEngine;
using System.Collections.Generic;

// This forces Unity to automatically add these components to the prefab
[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public class Vertex : MonoBehaviour
{
    [Header("Graph Data")]
    public List<Edge> connectedEdges = new List<Edge>();

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

    public void AddEdge(Edge newEdge)
    {
        if (!connectedEdges.Contains(newEdge))
        {
            connectedEdges.Add(newEdge);
        }
    }

    public void RemoveEdge(Edge edgeToRemove)
    {
        if (connectedEdges.Contains(edgeToRemove))
        {
            connectedEdges.Remove(edgeToRemove);
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
        foreach (Edge edge in connectedEdges)
        {
            if (edge != null && edge.GetOppositeVertex(this) != null)
            {
                Gizmos.DrawLine(transform.position, edge.GetOppositeVertex(this).transform.position);
            }
        }
    }
}