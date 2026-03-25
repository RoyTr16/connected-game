using UnityEngine;

public class CityBuilderCamera : MonoBehaviour
{
    private PlayerControls _controls;

    [Header("Panning")]
    public float panSpeed = 500f;
    [SerializeField] private float boostMultiplier = 2f;

    [Header("Zooming")]
    public float zoomSpeed = 100f;
    public float minHeight = 10f;   // NOTE: These should likely be POSITIVE numbers now!
    public float maxHeight = 1000f; // (e.g., hovering 10 to 1000 meters above the ground)

    [Header("Rotation")]
    public float rotationSpeed = 100f;

    [Header("Smoothing")]
    [Range(0.01f, 1f)]
    public float smoothing = 0.15f;

    private Vector3 _targetPosition;
    private float _targetYaw;   // Rotation around world Y-axis (perpendicular to X/Z floor)
    private float _targetPitch; // Tilt angle (0 = looking horizontal, 90 = looking straight down)

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

        // Extract initial pitch/yaw from the editor-set rotation
        Vector3 euler = transform.eulerAngles;

        // For a 3D XZ floor, pitch is still X, but yaw is now Y!
        _targetPitch = euler.x;
        _targetYaw = euler.y;

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

        // Calculate flattened forward and right vectors based ONLY on our Yaw.
        // This ensures panning slides perfectly parallel to the XZ floor.
        Quaternion yawOnly = Quaternion.AngleAxis(_targetYaw, Vector3.up);
        Vector3 right = yawOnly * Vector3.right;
        Vector3 forward = yawOnly * Vector3.forward;

        float speed = _controls.CityCamera.Boost.IsPressed() ? panSpeed * boostMultiplier : panSpeed;

        Vector3 move = (right * moveInput.x + forward * moveInput.y) * speed * Time.deltaTime;
        _targetPosition += move;

        // Clamp the Y axis (Height), not the Z axis
        _targetPosition.y = Mathf.Clamp(_targetPosition.y, minHeight, maxHeight);
    }

    private void HandleZooming()
    {
        float scroll = _controls.CityCamera.Zoom.ReadValue<float>();
        if (scroll == 0f) return;

        scroll = Mathf.Clamp(scroll, -1f, 1f);

        // Zoom pushes the camera along its actual 3D forward direction (pitch included)
        Vector3 forward = _currentRotation * Vector3.forward;
        _targetPosition += forward * scroll * zoomSpeed * Time.deltaTime;

        // Clamp the Y axis (Height)
        _targetPosition.y = Mathf.Clamp(_targetPosition.y, minHeight, maxHeight);
    }

    private void HandleRotation()
    {
        float twist = _controls.CityCamera.Twist.ReadValue<float>();
        if (twist != 0f)
            _targetYaw -= twist * rotationSpeed * Time.deltaTime;

        if (_controls.CityCamera.DragRotate.IsPressed())
        {
            Vector2 delta = _controls.CityCamera.PointerDelta.ReadValue<Vector2>();

            _targetYaw += delta.x * rotationSpeed * Time.deltaTime;
            _targetPitch -= delta.y * rotationSpeed * Time.deltaTime;
        }
    }

    private void ApplySmoothing()
    {
        transform.position = Vector3.Lerp(transform.position, _targetPosition, smoothing);

        // Build target rotation: yaw around world Y (UP), then pitch around local X
        Quaternion targetYaw = Quaternion.AngleAxis(_targetYaw, Vector3.up);
        Quaternion targetPitch = Quaternion.AngleAxis(_targetPitch, Vector3.right);
        Quaternion targetRotation = targetYaw * targetPitch;

        _currentRotation = Quaternion.Slerp(_currentRotation, targetRotation, smoothing);
        transform.rotation = _currentRotation;
    }
}