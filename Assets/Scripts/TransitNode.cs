using UnityEngine;
using System.Collections.Generic;

public class TransitNode : MonoBehaviour
{
    private SpriteRenderer _spriteRenderer;
    private Color _originalColor;

    public Color selectedColor = Color.yellow;
    public bool isSelected { get; private set; } = false;

    // NEW: The Adjacency List (Who is connected to this node by a direct road?)
    public List<TransitNode> neighbors = new List<TransitNode>();

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalColor = _spriteRenderer.color;
    }

    public void ToggleSelection()
    {
        isSelected = !isSelected;
        _spriteRenderer.color = isSelected ? selectedColor : _originalColor;
    }

    // NEW: This draws red debug lines in the Unity Editor to prove the graph is connected
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        foreach (TransitNode neighbor in neighbors)
        {
            if (neighbor != null)
            {
                // Draw a line from this node to its neighbor
                Gizmos.DrawLine(transform.position, neighbor.transform.position);
            }
        }
    }
}