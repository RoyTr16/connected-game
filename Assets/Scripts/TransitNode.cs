using UnityEngine;
using System.Collections.Generic;

public class TransitNode : MonoBehaviour
{
    private SpriteRenderer _spriteRenderer;
    private Color _originalColor;

    public Color selectedColor = Color.yellow;
    public bool isSelected { get; private set; } = false;

    public List<TransitNode> neighbors = new List<TransitNode>();

    // NEW: This dictionary remembers the physical curved path to each neighbor
    public Dictionary<TransitNode, List<Vector3>> pathGeometry = new Dictionary<TransitNode, List<Vector3>>();

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

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        foreach (TransitNode neighbor in neighbors)
        {
            if (neighbor != null)
            {
                Gizmos.DrawLine(transform.position, neighbor.transform.position);
            }
        }
    }
}