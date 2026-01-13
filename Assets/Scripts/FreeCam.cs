using NUnit.Framework.Internal.Commands;
using UnityEngine;

public class Camera : MonoBehaviour
{
    [Header("Toggle")]
    public KeyCode toggleFreeCamKey = KeyCode.F3;
    public bool startEnabled = false;

    [Header("Movement")]
    public float moveSpeed = 8f;
    public float speedMultiplier = 3f;
    public float slowMultiplier = 0.25f;
    public float verticalSpeed = 6f;

    [Header("Look")]
    public float lookSensitivity = 2f;
    public bool invertY = false;
    public float pitchClamp = 89f;

    [Header("Smoothing")]
    public bool smooth = true;
    public float moveSmoothTime = 0.05f;
    public float lookSmoothTime = 0.03f;

    //[Header("Cursor")]
    //public bool lockCursor = true;
    //public KeyCode toggleCursorKey = KeyCode.Escape;

    [Header("Cursor (when FreeCam is ON)")]
    public bool lockCursorWhenEnabled = true;

    bool freeCamEnabled;

    float yaw;
    float pitch;

    Vector3 moveVelocity;
    Vector2 lookVelocity;
    Vector3 currentMove;
    Vector2 currentLook;

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x;

        SetFreeCam(startEnabled);
        //ApplyCursorLock(lockCursor);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleFreeCamKey))
        {
            SetFreeCam(!freeCamEnabled);
        }

        if (!freeCamEnabled)
            return;

        if (Cursor.lockState == CursorLockMode.Locked)
            HandleLook();

        HandleMove();
    }

    void SetFreeCam(bool enabled)
    {
        freeCamEnabled = enabled;

        moveVelocity = Vector3.zero;
        lookVelocity = Vector2.zero;
        currentMove = Vector3.zero;
        currentLook = Vector2.zero;

        if (enabled && lockCursorWhenEnabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void HandleLook()
    {
        float mx = Input.GetAxisRaw("Mouse X") * lookSensitivity;
        float my = Input.GetAxisRaw("Mouse Y") * lookSensitivity * (invertY ? 1f : -1f);

        Vector2 targetLook = new Vector2(mx, my);

        currentLook = smooth
             ? Vector2.SmoothDamp(currentLook, targetLook, ref lookVelocity, lookSmoothTime)
             : targetLook;

        yaw += currentLook.x;
        pitch += currentLook.y; 
        pitch = Mathf.Clamp(pitch, -pitchClamp, pitchClamp);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void HandleMove()
    {
        float speed = moveSpeed;

        if (Input.GetKey(KeyCode.LeftShift)) speed *= speedMultiplier;
        if (Input.GetKey(KeyCode.LeftControl)) speed *= slowMultiplier;

        // WASD movement
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");

        // Up & Down
        float y = 0f;
        if (Input.GetKey(KeyCode.E)) y += 1f;
        if (Input.GetKey(KeyCode.Q)) y -= 1f;

        Vector3 input = new Vector3(x, y * (verticalSpeed / Mathf.Max(0.0001f, moveSpeed)), z);
        input = Vector3.ClampMagnitude(input, 1f);

        Vector3 targetMove = transform.TransformDirection(input) * speed;

        if (smooth)
        {
            currentMove = Vector3.SmoothDamp(currentMove, targetMove, ref moveVelocity, moveSmoothTime);
            transform.position += currentMove * Time.deltaTime;
        }
        else
        {
            transform.position += targetMove * Time.deltaTime;
        }
    }

  /* void ApplyCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }*/
}
