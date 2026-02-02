using NUnit.Framework.Internal.Commands;
using UnityEngine;

public class Camera : MonoBehaviour
{
    [Header("Toggle")]
    public KeyCode toggleFreeCamKey = KeyCode.F3; // Key to enable/disable the free cam at runtiem
    public bool startEnabled = false;

    [Header("Movement")]
    public float moveSpeed = 8f; // Base movement speed (unit per second)
    public float speedMultiplier = 3f; // Multiplier when the user holds shift to speed camera
    public float slowMultiplier = 0.25f; // Multiplier when the user holds Left Ctrl to slow camera
    public float verticalSpeed = 6f; // How fast the user can go up and down

    [Header("Look")]
    public float lookSensitivity = 2f; // Camera user sensitivity 
    public bool invertY = false;
    public float pitchClamp = 89f; // Max up/down angle

    [Header("Smoothing")]
    public bool smooth = true; // Camera and movement smoothing
    public float moveSmoothTime = 0.05f;
    public float lookSmoothTime = 0.03f;

    //[Header("Cursor")]
    //public bool lockCursor = true;
    //public KeyCode toggleCursorKey = KeyCode.Escape;

    [Header("Cursor (when FreeCam is ON)")]
    public bool lockCursorWhenEnabled = true;

    bool freeCamEnabled; // whether freecam is currently active

    float yaw; // rotation around Y (left/right)
    float pitch; // rotation around X (up/down)

    // Movement smoothing vector3 velocity
    Vector3 moveVelocity;
    Vector2 lookVelocity;

    // Smoothed values that are applied to movement/look each frame
    Vector3 currentMove;
    Vector2 currentLook;

    void Start()
    {
        Vector3 e = transform.eulerAngles; // Read the starting rotation of the GameObject this script is attached to, stored in Euler angles (degrees)
        yaw = e.y; // left/right
        pitch = e.x; // up/down

        SetFreeCam(startEnabled); // Enable or disable freecam + cursor lock state based on startEnabled
        //ApplyCursorLock(lockCursor);
    }

    void Update()
    {
        // Toggle freecam when key is pressed
        if (Input.GetKeyDown(toggleFreeCamKey))
        {
            SetFreeCam(!freeCamEnabled);
        }

        // If freecam is off, do nothing else
        if (!freeCamEnabled)
            return;

        // Only allow clooking around if the cursor is locked
        if (Cursor.lockState == CursorLockMode.Locked)
            HandleLook();

        HandleMove(); // Movement is handled regardless (user can still move without locking camera)
    }

    // Toggle Freecam function
    void SetFreeCam(bool enabled)
    {
        freeCamEnabled = enabled; // Active state

        // reset smoothing data so camera doesn't keep the same values when toggling on/off from before
        moveVelocity = Vector3.zero;
        lookVelocity = Vector2.zero;
        currentMove = Vector3.zero;
        currentLook = Vector2.zero;

        // If enabling freecam and configured to lock cursor:
        if (enabled && lockCursorWhenEnabled)
        {
            // Lock cursor to center and hide it (first person camera)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            // Else allow cursor to move free and visible
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void HandleLook()
    {
        // Read raw mouse movement this frame (no smoothing)
        float mx = Input.GetAxisRaw("Mouse X") * lookSensitivity;

        // Mouse Y inverted (moving mouse up looks up), applies inversion using "invertY":
        float my = Input.GetAxisRaw("Mouse Y") * lookSensitivity * (invertY ? 1f : -1f);

        // Target look delta for this frame (how much to add to yaw/pitch)
        Vector2 targetLook = new Vector2(mx, my);

        // Smooth mouse movement + smooth dampening aprochaes targetLook over lookSmoothTime
        currentLook = smooth
             ? Vector2.SmoothDamp(currentLook, targetLook, ref lookVelocity, lookSmoothTime)
             : targetLook;

        // Apply to yaw and pitch accumulators:
        yaw += currentLook.x;
        pitch += currentLook.y; 

        // Clamp pitch so you can't rotate passed straight up/down
        pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

        // Convert yaw/pitch back into a Quaternion rotation and apply to transform of camera:
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void HandleMove()
    {
        float speed = moveSpeed; // Start from base speed

        if (Input.GetKey(KeyCode.LeftShift)) speed *= speedMultiplier; // Apply shift speed
        if (Input.GetKey(KeyCode.LeftControl)) speed *= slowMultiplier; // Apply Left Ctrl speed

        // WASD movement
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        // Up & Down
        float y = 0f;
        if (Input.GetKey(KeyCode.E)) y += 1f; // up
        if (Input.GetKey(KeyCode.Q)) y -= 1f; // down

        // Build input vector in local space:
        Vector3 input = new Vector3(x, y * (verticalSpeed / Mathf.Max(0.0001f, moveSpeed)), z); // x = strafe, z = forward/back, y = vertical
        input = Vector3.ClampMagnitude(input, 1f); // prevent faster diagonal movement (W+A or W+D would otherwise be > 1 length).

        // Convert local direction into world direction base on current cam rotation
        Vector3 targetMove = transform.TransformDirection(input) * speed; // "forward" will moves where the camera is facing

        // APply either smooth movement or immediate movement:
        if (smooth)
        {
            // Smooth currentMove toward targetMove, moveVelocity is used internally by SmoothDamp to track smoothing state.
            currentMove = Vector3.SmoothDamp(currentMove, targetMove, ref moveVelocity, moveSmoothTime);
            transform.position += currentMove * Time.deltaTime; // Apply movement scaled by deltaTime
        }
        else
        {
            transform.position += targetMove * Time.deltaTime; // No smoothing: just move immediately based on target
        }
    }

  /* void ApplyCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }*/
}
