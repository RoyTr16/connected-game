using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch; // Specify we mean the new Touch API

public class CameraController : MonoBehaviour
{
    private PlayerControls _controls;
    private Camera _cam;

    [Header("PC Settings")]
    public float pcPanSpeed = 500f;
    public float pcZoomSpeed = 100f;

    [Header("Mobile Settings")]
    public float touchPanSpeed = 0.5f;
    public float touchZoomSpeed = 0.5f;

    [Header("Camera Limits")]
    public float minZoom = 200f;   // Closest to the street
    public float maxZoom = 3000f;  // Farthest pulled back

    private void Awake()
    {
        _controls = new PlayerControls();
        _cam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        _controls.Gameplay.Enable();
        // Turn on Unity's multi-touch tracking system
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        _controls.Gameplay.Disable();
        EnhancedTouchSupport.Disable();
    }

    private void Update()
    {
        // If fingers are on the screen, handle mobile gestures.
        // Otherwise, listen for PC keyboard/mouse.
        if (Touch.activeTouches.Count > 0)
        {
            HandleMobileControls();
        }
        else
        {
            HandlePCControls();
        }
    }

    private void HandlePCControls()
    {
        // --- PC PANNING (WASD) ---
        Vector2 moveInput = _controls.Gameplay.Pan.ReadValue<Vector2>();
        float currentSpeed = pcPanSpeed * (_cam.orthographicSize / 1000f); // Move faster when zoomed out
        Vector3 moveDirection = new Vector3(moveInput.x, moveInput.y, 0f);
        transform.position += moveDirection * currentSpeed * Time.deltaTime;

        // --- PC ZOOMING (Scroll Wheel) ---
        float scrollInput = _controls.Gameplay.Zoom.ReadValue<float>();
        if (scrollInput != 0)
        {
            float newSize = _cam.orthographicSize - (scrollInput * pcZoomSpeed * Time.deltaTime);
            _cam.orthographicSize = Mathf.Clamp(newSize, minZoom, maxZoom);
        }
    }

    private void HandleMobileControls()
    {
        // --- MOBILE PANNING (1-Finger Drag) ---
        if (Touch.activeTouches.Count == 1)
        {
            Touch touch = Touch.activeTouches[0];

            // We subtract the delta so the camera moves opposite to the finger swipe,
            // making it feel like you are physically dragging the map.
            Vector2 delta = touch.delta * touchPanSpeed * (_cam.orthographicSize / 1000f);
            transform.position -= new Vector3(delta.x, delta.y, 0f);
        }

        // --- MOBILE ZOOMING (2-Finger Pinch) ---
        else if (Touch.activeTouches.Count == 2)
        {
            Touch touchZero = Touch.activeTouches[0];
            Touch touchOne = Touch.activeTouches[1];

            // 1. Find the position of both fingers in the previous frame
            Vector2 touchZeroPrevPos = touchZero.screenPosition - touchZero.delta;
            Vector2 touchOnePrevPos = touchOne.screenPosition - touchOne.delta;

            // 2. Find the distance between the fingers in the previous frame vs the current frame
            float prevMagnitude = (touchZeroPrevPos - touchOnePrevPos).magnitude;
            float currentMagnitude = (touchZero.screenPosition - touchOne.screenPosition).magnitude;

            // 3. The difference is our zoom amount (Negative = pinching in, Positive = spreading out)
            float difference = currentMagnitude - prevMagnitude;

            // Apply the zoom (We subtract so spreading fingers zooms IN, lowering orthographic size)
            float newSize = _cam.orthographicSize - (difference * touchZoomSpeed);
            _cam.orthographicSize = Mathf.Clamp(newSize, minZoom, maxZoom);
        }
    }
}