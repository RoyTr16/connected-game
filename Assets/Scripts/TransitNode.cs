using UnityEngine;

public class TransitNode : MonoBehaviour
{
    private SpriteRenderer _spriteRenderer;
    private Color _originalColor;

    [Tooltip("The color the node turns when clicked")]
    public Color selectedColor = Color.yellow;

    // A simple flag to track if this specific node is currently active
    public bool isSelected { get; private set; } = false;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalColor = _spriteRenderer.color;
    }

    public void ToggleSelection()
    {
        isSelected = !isSelected;

        // Swap the color
        _spriteRenderer.color = isSelected ? selectedColor : _originalColor;

        if (isSelected)
        {
            Debug.Log($"Selected {gameObject.name} at {transform.position}");
        }
    }
}