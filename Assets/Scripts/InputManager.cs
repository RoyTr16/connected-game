using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    private PlayerControls _controls;
    private Camera _mainCamera;
    private bool _isDragging = false;

    private void Awake()
    {
        _controls = new PlayerControls();
        _mainCamera = Camera.main;

        _controls.Gameplay.Interact.started += ctx => OnInteractStarted();
        _controls.Gameplay.Interact.canceled += ctx => OnInteractEnded();
    }

    private void OnEnable() => _controls.Gameplay.Enable();
    private void OnDisable() => _controls.Gameplay.Disable();

    private void OnInteractStarted()
    {
        Vector3 worldPos = GetMouseWorldPosition();

        // Use a generous 10-meter search radius, and check EVERYTHING inside it
        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, 10f);

        foreach (Collider2D hit in hits)
        {
            Vertex node = hit.GetComponent<Vertex>();
            if (node != null)
            {
                // We found a node! Start the route and stop looking.
                _isDragging = true;
                LineManager.Instance.StartRoute(node);
                return;
            }
        }
    }

    private void OnInteractEnded()
    {
        _isDragging = false;
        LineManager.Instance.FinishRoute();
    }

    private void Update()
    {
        if (_isDragging)
        {
            // Just continuously feed the mouse position to the LineManager
            LineManager.Instance.UpdatePreviewLine(GetMouseWorldPosition());
        }
    }

    private Vector3 GetMouseWorldPosition()
    {
        Vector2 screenPosition = _controls.Gameplay.PointerPosition.ReadValue<Vector2>();
        Vector3 worldPosition = _mainCamera.ScreenToWorldPoint(screenPosition);
        worldPosition.z = 0f; // Force the coordinate flat
        return worldPosition;
    }
}