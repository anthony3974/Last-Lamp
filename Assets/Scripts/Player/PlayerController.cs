using UnityEngine;
using UnityEngine.InputSystem;

namespace LastLamp
{
    /// <summary>
    /// Third-person walk, sprint, crouch and jump. Movement follows the camera's facing, and the
    /// camera target is turned here so Cinemachine can follow it.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] InputActionAsset actions;
        [SerializeField] Transform cameraTarget;
        [SerializeField] Transform body;
        [Tooltip("What can stop the player standing up from a crouch.")]
        [SerializeField] LayerMask obstacleMask;

        [Header("Speed (metres per second)")]
        [SerializeField] float walkSpeed = 3f;
        [SerializeField] float sprintSpeed = 5.5f;
        [SerializeField] float crouchSpeed = 1.5f;
        [SerializeField] float acceleration = 20f;
        [SerializeField] float turnSpeed = 720f;

        [Header("Jump")]
        [SerializeField] float jumpHeight = 1f;
        [SerializeField] float gravity = 20f;

        [Header("Height (metres)")]
        [SerializeField] float standHeight = 1.8f;
        [SerializeField] float crouchHeight = 1.1f;
        [SerializeField] float heightChangeSpeed = 5f;
        [SerializeField] float standCameraHeight = 1.55f;
        [SerializeField] float crouchCameraHeight = 0.95f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float stickSpeed = 150f;
        [SerializeField] float minPitch = -35f;
        [SerializeField] float maxPitch = 60f;

        CharacterController controller;
        InputActionMap map;
        InputAction move;
        InputAction look;
        InputAction jump;
        InputAction sprint;
        InputAction crouch;

        Vector3 horizontalVelocity;
        float verticalVelocity;
        float currentHeight;
        float yaw;
        float pitch;
        bool isCrouching;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            map = actions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            jump = map.FindAction("Jump", true);
            sprint = map.FindAction("Sprint", true);
            crouch = map.FindAction("Crouch", true);

            currentHeight = standHeight;
            yaw = transform.eulerAngles.y;
        }

        void OnEnable()
        {
            map.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            UpdateLook();
            UpdateHeight();
            UpdateMovement();
            cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void UpdateLook()
        {
            Vector2 input = look.ReadValue<Vector2>();
            bool usingStick = look.activeControl?.device is Gamepad;
            float scale = usingStick ? stickSpeed * Time.deltaTime : mouseSensitivity;

            yaw += input.x * scale;
            pitch = Mathf.Clamp(pitch - input.y * scale, minPitch, maxPitch);
        }

        void UpdateHeight()
        {
            isCrouching = crouch.IsPressed() || (isCrouching && !CanStand());

            float target = isCrouching ? crouchHeight : standHeight;
            currentHeight = Mathf.MoveTowards(currentHeight, target, heightChangeSpeed * Time.deltaTime);

            controller.height = currentHeight;
            controller.center = Vector3.up * (currentHeight / 2f);
            body.localScale = new Vector3(1f, currentHeight / standHeight, 1f);

            float amount = Mathf.InverseLerp(crouchHeight, standHeight, currentHeight);
            cameraTarget.localPosition = Vector3.up * Mathf.Lerp(crouchCameraHeight, standCameraHeight, amount);
        }

        bool CanStand()
        {
            float radius = controller.radius;
            Vector3 from = transform.position + Vector3.up * (crouchHeight - radius);
            Vector3 to = transform.position + Vector3.up * (standHeight - radius);
            return !Physics.CheckCapsule(from, to, radius - 0.02f, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        void UpdateMovement()
        {
            Vector2 input = move.ReadValue<Vector2>();
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            wish = Vector3.ClampMagnitude(wish, 1f);

            float speed = isCrouching ? crouchSpeed : sprint.IsPressed() ? sprintSpeed : walkSpeed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wish * speed, acceleration * Time.deltaTime);

            if (controller.isGrounded)
            {
                if (verticalVelocity < 0f) verticalVelocity = -2f;
                if (jump.WasPressedThisFrame() && !isCrouching) verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            verticalVelocity -= gravity * Time.deltaTime;

            controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);

            if (wish.sqrMagnitude > 0.01f)
            {
                var facing = Quaternion.LookRotation(wish);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * Time.deltaTime);
            }
        }
    }
}
