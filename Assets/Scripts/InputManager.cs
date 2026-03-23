using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    private PlayerControls _controls;
    private Camera _mainCamera;

    private bool _isRouting = false;
    private bool _isDragging = false;

    private void Awake()
    {
        _controls = new PlayerControls();
        _mainCamera = Camera.main;

        // Mouse Down & Up
        _controls.Gameplay.Interact.started += ctx => OnInteractStarted();
        _controls.Gameplay.Interact.canceled += ctx => OnInteractEnded();

        // Explicit Actions
        _controls.Gameplay.Undo.performed += ctx => OnUndoPerformed();
        _controls.Gameplay.Finalize.performed += ctx => OnFinalizePerformed();
    }

    private void OnEnable() => _controls.Gameplay.Enable();
    private void OnDisable() => _controls.Gameplay.Disable();

    private void OnInteractStarted()
    {
        Vector3 worldPos = GetMouseWorldPosition();

        if (!_isRouting)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, 10f);
            foreach (Collider2D hit in hits)
            {
                Vertex node = hit.GetComponent<Vertex>();
                if (node != null)
                {
                    _isRouting = true;
                    _isDragging = true;
                    LineManager.Instance.StartRoute(node);
                    return;
                }
            }
        }
        else
        {
            _isDragging = true;
            LineManager.Instance.CommitAStarWaypoint();
        }
    }

    private void OnInteractEnded()
    {
        if (_isRouting && _isDragging)
        {
            _isDragging = false;
            LineManager.Instance.CommitDragWaypoint(GetMouseWorldPosition());
        }
    }

    private void OnUndoPerformed()
    {
        if (!_isRouting) return;

        // If they right-click while actively dragging, just cancel the drag preview
        if (_isDragging)
        {
            _isDragging = false;
            LineManager.Instance.ClearPreview();
        }
        // If they right-click while hovering, erase the last locked waypoint
        else
        {
            bool keepRouting = LineManager.Instance.UndoLastWaypoint();

            // If that was the last waypoint, shut down the routing state completely
            if (!keepRouting)
            {
                _isRouting = false;
                _isDragging = false;
            }
        }
    }

    private void OnFinalizePerformed()
    {
        if (_isRouting)
        {
            _isRouting = false;
            _isDragging = false;
            LineManager.Instance.FinishRoute();
        }
    }

    private void Update()
    {
        if (!_isRouting) return;

        Vector3 worldPos = GetMouseWorldPosition();

        if (_isDragging) LineManager.Instance.UpdateDragPreview(worldPos);
        else LineManager.Instance.UpdateAStarPreview(worldPos);
    }

    private Vector3 GetMouseWorldPosition()
    {
        Vector2 screenPosition = _controls.Gameplay.PointerPosition.ReadValue<Vector2>();
        Vector3 worldPosition = _mainCamera.ScreenToWorldPoint(screenPosition);
        worldPosition.z = 0f;
        return worldPosition;
    }
}