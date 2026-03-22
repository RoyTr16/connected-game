using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    private PlayerControls _controls;
    private Camera _mainCamera;

    private void Awake()
    {
        _controls = new PlayerControls();
        _mainCamera = Camera.main;

        // When the player clicks/taps, run the OnInteract function
        _controls.Gameplay.Interact.performed += OnInteract;
    }

    private void OnEnable() => _controls.Gameplay.Enable();
    private void OnDisable() => _controls.Gameplay.Disable();

    private void OnInteract(InputAction.CallbackContext context)
    {
        // 1. Get the screen pixel coordinate
        Vector2 screenPosition = _controls.Gameplay.PointerPosition.ReadValue<Vector2>();

        // 2. Convert to world coordinates
        Vector3 worldPosition = _mainCamera.ScreenToWorldPoint(screenPosition);

        // We cast it to a Vector2 because 2D Physics doesn't care about Z-axis depth
        Vector2 worldPosition2D = new Vector2(worldPosition.x, worldPosition.y);

        // 3. Fire a 2D Raycast exactly at that point
        // Vector2.zero means the ray doesn't travel; it just checks that exact pin-drop location.
        RaycastHit2D hit = Physics2D.Raycast(worldPosition2D, Vector2.zero);

        // 4. Did we hit anything with a collider?
        if (hit.collider != null)
        {
            // Try to find the TransitNode script on the object we just poked
            TransitNode node = hit.collider.GetComponent<TransitNode>();

            if (node != null)
            {
                node.ToggleSelection();
            }
        }
    }
}