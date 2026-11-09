using System;
using Unity.Cinemachine;
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
        [Tooltip("Seconds in the air before touching down counts as a landing.")]
        [SerializeField] float minAirTime = 0.15f;

        [Header("Dodge")]
        [SerializeField] float dodgeSpeed = 11f;
        [SerializeField] float dodgeSeconds = 0.22f;
        [SerializeField] float dodgeCooldown = 1.2f;

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
        InputAction interact;
        InputAction attack;
        InputAction dodge;

        Vector3 horizontalVelocity;
        Vector3 dodgeDirection;
        float dodgeTimeLeft;
        float dodgeCooldownLeft;
        float verticalVelocity;
        float currentHeight;
        float yaw;
        float pitch;
        bool isCrouching;
        bool isSprinting;
        float airTime;

        public event Action Landed;

        public float HorizontalSpeed => horizontalVelocity.magnitude;
        public float SprintSpeed => sprintSpeed;
        public bool IsCrouching => isCrouching;
        public bool IsSprinting => isSprinting;
        public bool IsAirborne => !controller.isGrounded;
        public bool IsHidden { get; private set; }

        // The Interact action has a Hold interaction, but going into hiding has to be instant.
        public bool InteractPressed => interact.WasPressedThisFrame();
        public bool AttackPressed => attack.WasPressedThisFrame();
        public bool IsDodging => dodgeTimeLeft > 0f;
        public Vector3 LookDirection => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            map = actions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            jump = map.FindAction("Jump", true);
            sprint = map.FindAction("Sprint", true);
            crouch = map.FindAction("Crouch", true);
            interact = map.FindAction("Interact", true);
            attack = map.FindAction("Attack", true);
            dodge = map.FindAction("Dodge", true);

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
            if (!IsHidden)
            {
                UpdateHeight();
                UpdateMovement();
            }
            cameraTarget.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>Moves the player out of the world to the given position. They can still look around.</summary>
        public void EnterHiding(Vector3 position)
        {
            IsHidden = true;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            controller.enabled = false;
            body.gameObject.SetActive(false);
            transform.position = position;
        }

        public void LeaveHiding(Vector3 position)
        {
            PlaceAt(position);
        }

        /// <summary>Sends the player to a far-off point at once, from wherever they are, hidden or not.</summary>
        public void Respawn(Vector3 position)
        {
            Vector3 jump = position - transform.position;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            dodgeTimeLeft = 0f;
            airTime = 0f;
            PlaceAt(position);
            CinemachineCore.OnTargetObjectWarped(cameraTarget, jump);
        }

        void PlaceAt(Vector3 position)
        {
            IsHidden = false;
            controller.enabled = false;
            transform.position = position;
            body.gameObject.SetActive(true);
            controller.enabled = true;
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

            isSprinting = !isCrouching && sprint.IsPressed();
            float speed = isCrouching ? crouchSpeed : isSprinting ? sprintSpeed : walkSpeed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, wish * speed, acceleration * Time.deltaTime);

            if (controller.isGrounded)
            {
                if (verticalVelocity < 0f) verticalVelocity = -2f;
                if (jump.WasPressedThisFrame() && !isCrouching) verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            }
            verticalVelocity -= gravity * Time.deltaTime;
            UpdateDodge(wish);

            Vector3 flatVelocity = IsDodging ? dodgeDirection * dodgeSpeed : horizontalVelocity;
            controller.Move((flatVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
            TrackLanding();

            if (wish.sqrMagnitude > 0.01f)
            {
                var facing = Quaternion.LookRotation(wish);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * Time.deltaTime);
            }
        }

        // Dodges toward the movement keys, or straight back when standing still.
        void UpdateDodge(Vector3 wish)
        {
            dodgeTimeLeft = Mathf.Max(0f, dodgeTimeLeft - Time.deltaTime);
            dodgeCooldownLeft = Mathf.Max(0f, dodgeCooldownLeft - Time.deltaTime);
            if (!dodge.WasPressedThisFrame() || dodgeCooldownLeft > 0f || !controller.isGrounded) return;

            dodgeDirection = wish.sqrMagnitude > 0.01f ? wish.normalized : -LookDirection;
            dodgeTimeLeft = dodgeSeconds;
            dodgeCooldownLeft = dodgeCooldown;
        }

        void TrackLanding()
        {
            if (!controller.isGrounded)
            {
                airTime += Time.deltaTime;
                return;
            }

            if (airTime >= minAirTime) Landed?.Invoke();
            airTime = 0f;
        }
    }
}
