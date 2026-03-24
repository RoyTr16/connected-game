using UnityEngine;
using UnityEngine.InputSystem;

public class CityBuilderCamera : MonoBehaviour
{
    private PlayerControls _controls;

    [Header("Panning")]
    public float panSpeed = 500f;
    [SerializeField] private float boostMultiplier = 2f;

    [Header("Zooming")]
    public float zoomSpeed = 100f;
    public float minHeight = -3000f;
    public float maxHeight = -200f;

    [Header("Rotation")]
    public float rotationSpeed = 100f;

    [Header("Smoothing")]
    [Range(0.01f, 1f)]
    public float smoothing = 0.15f;

    private Vector3 _targetPosition;
    private float _targetYaw;   // Rotation around world Z-axis (perpendicular to X/Y floor)
    private float _targetPitch; // Tilt angle (0 = looking straight down, 90 = looking horizontal)

    private Quaternion _currentRotation;

    private void Awake()
    {
        _controls = new PlayerControls();
    }

    private void OnEnable()
    {
        _controls.CityCamera.Enable();
    }

    private void OnDisable()
    {
        _controls.CityCamera.Disable();
    }

    private void Start()
    {
        _targetPosition = transform.position;

        // Extract initial pitch/yaw from the editor-set rotation so we don't override it
        Vector3 euler = transform.eulerAngles;
        // The quaternion composition is: yaw around Z, then pitch around X
        // For a camera looking down at the X/Y plane, the editor X rotation maps to pitch
        _targetPitch = euler.x;
        _targetYaw = euler.z;

        _currentRotation = transform.rotation;
    }

    private void Update()
    {
        HandlePanning();
        HandleZooming();
        HandleRotation();
        ApplySmoothing();
    }

    private void HandlePanning()
    {
        Vector2 moveInput = _controls.CityCamera.Pan.ReadValue<Vector2>();
        if (moveInput == Vector2.zero) return;

        // W/S (moveInput.y) moves along the camera's forward direction (into the scene)
        // A/D (moveInput.x) moves along the camera's right direction, flattened to X/Y
        Quaternion yawOnly = Quaternion.AngleAxis(_targetYaw, Vector3.forward);
        Vector3 right = yawOnly * Vector3.right;

        // Forward = camera's actual forward so W pushes into the scene
        Vector3 forward = _currentRotation * Vector3.forward;

        float speed = _controls.CityCamera.Boost.IsPressed() ? panSpeed * boostMultiplier : panSpeed;

        Vector3 move = (right * moveInput.x + forward * moveInput.y) * speed * Time.deltaTime;
        _targetPosition += move;
        _targetPosition.z = Mathf.Clamp(_targetPosition.z, minHeight, maxHeight);
    }

    private void HandleZooming()
    {
        float scroll = _controls.CityCamera.Zoom.ReadValue<float>();
        if (scroll == 0f) return;

        // Clamp raw scroll to prevent huge jumps from trackpads
        scroll = Mathf.Clamp(scroll, -1f, 1f);

        // Push the camera along its forward direction
        Vector3 forward = _currentRotation * Vector3.forward;
        _targetPosition += forward * scroll * zoomSpeed * Time.deltaTime;
        _targetPosition.z = Mathf.Clamp(_targetPosition.z, minHeight, maxHeight);
    }

    private void HandleRotation()
    {
        // Q/E keys for yaw (orbit left/right around the map)
        float twist = _controls.CityCamera.Twist.ReadValue<float>();
        if (twist != 0f)
            _targetYaw += twist * rotationSpeed * Time.deltaTime;

        // Middle mouse drag: yaw (left/right) and pitch (up/down)
        if (_controls.CityCamera.DragRotate.IsPressed())
        {
            Vector2 delta = _controls.CityCamera.PointerDelta.ReadValue<Vector2>();

            _targetYaw -= delta.x * rotationSpeed * Time.deltaTime;
            _targetPitch -= delta.y * rotationSpeed * Time.deltaTime;
        }
    }

    private void ApplySmoothing()
    {
        transform.position = Vector3.Lerp(transform.position, _targetPosition, smoothing);

        // Build target rotation: yaw around world Z (floor normal), then pitch around local X
        Quaternion targetYaw = Quaternion.AngleAxis(_targetYaw, Vector3.forward);
        Quaternion targetPitch = Quaternion.AngleAxis(_targetPitch, Vector3.right);
        Quaternion targetRotation = targetYaw * targetPitch;

        _currentRotation = Quaternion.Slerp(_currentRotation, targetRotation, smoothing);
        transform.rotation = _currentRotation;
    }
}
