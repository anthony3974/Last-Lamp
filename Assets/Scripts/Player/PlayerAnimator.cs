using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Feeds the player's movement to the character's Animator. The animation never moves the player: PlayerController does.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Crouching = Animator.StringToHash("Crouching");
        static readonly int Airborne = Animator.StringToHash("Airborne");
        static readonly int Jump = Animator.StringToHash("Jump");
        static readonly int Dodge = Animator.StringToHash("Dodge");
        static readonly int Punch = Animator.StringToHash("Punch");

        // Short enough to feel instant, long enough not to flicker on stairs and slopes.
        const float AirborneAfter = 0.1f;
        const float SpeedDamping = 0.08f;
        // A footstep only counts from the clip that is mostly playing, or two clips in a blend would both sound.
        const float MinStepWeight = 0.5f;

        [Tooltip("How far the hips drop while crouch-walking, in metres. The clip stands much taller than the crouched idle.")]
        [SerializeField] float crouchWalkDrop = 0.25f;

        [Header("Look ahead while crouch-walking")]
        [Tooltip("The crouch-walk clip looks at the floor. This turns the head up toward a point this far ahead, in metres, at head height.")]
        [SerializeField] float lookAheadDistance = 4f;
        [Tooltip("How much the head turns to look ahead, 0 to 1.")]
        [Range(0f, 1f)] [SerializeField] float lookAheadHead = 1f;
        [Tooltip("How much the upper body helps the head look ahead, 0 to 1. Keep it low so the crouch still reads.")]
        [Range(0f, 1f)] [SerializeField] float lookAheadBody = 0.15f;
        [Tooltip("How long the head takes to come up or go back down, in seconds, so standing up blends with no snap.")]
        [SerializeField] float lookAheadFade = 0.25f;

        Animator animator;
        PlayerController player;
        PlayerCombat combat;
        PlayerNoise noise;
        bool wasDodging;
        float lookAhead;

        void Awake()
        {
            animator = GetComponent<Animator>();
            player = GetComponentInParent<PlayerController>();
            combat = GetComponentInParent<PlayerCombat>();
            noise = GetComponentInParent<PlayerNoise>();
        }

        void OnEnable()
        {
            player.Jumped += OnJumped;
            combat.Punched += OnPunched;
        }

        void OnDisable()
        {
            player.Jumped -= OnJumped;
            combat.Punched -= OnPunched;
        }

        void OnJumped() => animator.SetTrigger(Jump);

        void OnPunched() => animator.SetTrigger(Punch);

        void Update()
        {
            animator.SetFloat(Speed, player.HorizontalSpeed, SpeedDamping, Time.deltaTime);
            animator.SetBool(Crouching, player.IsCrouching);
            animator.SetBool(Airborne, player.AirTime > AirborneAfter);

            if (player.IsDodging && !wasDodging) animator.SetTrigger(Dodge);
            wasDodging = player.IsDodging;

            float target = CrouchWalkAmount(player.IsCrouching, animator.GetFloat(Speed), player.CrouchSpeed);
            lookAhead = FadeTowards(lookAhead, target, lookAheadFade, Time.deltaTime);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0) return;

            float drop = HipDrop(player.IsCrouching, animator.GetFloat(Speed), player.CrouchSpeed, crouchWalkDrop);
            LookAhead();
            if (drop <= 0f) return;

            // Lower the body while the feet stay where the clip put them, so the knees bend instead of the feet sinking.
            PinFoot(AvatarIKGoal.LeftFoot);
            PinFoot(AvatarIKGoal.RightFoot);
            animator.bodyPosition += Vector3.down * drop;
        }

        // Turns the head up from the floor toward a point ahead at head height, faded in with the crouch walk.
        void LookAhead()
        {
            if (lookAhead <= 0f)
            {
                animator.SetLookAtWeight(0f);
                return;
            }

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            // The bones still hold last frame's final pose here, hip drop included, which is close enough for a point metres away.
            animator.SetLookAtPosition(head.position + forward * lookAheadDistance);
            animator.SetLookAtWeight(lookAhead, lookAheadBody, lookAheadHead, 0f, 0.5f);
        }

        void PinFoot(AvatarIKGoal foot)
        {
            animator.SetIKPosition(foot, animator.GetIKPosition(foot));
            animator.SetIKPositionWeight(foot, 1f);
        }

        /// <summary>Called by the animation as a foot lands.</summary>
        public void FootStep(AnimationEvent step)
        {
            if (step.animatorClipInfo.weight >= MinStepWeight) noise.PlayFootstep();
        }

        internal static float HipDrop(bool crouching, float speed, float crouchSpeed, float maxDrop)
        {
            return maxDrop * CrouchWalkAmount(crouching, speed, crouchSpeed);
        }

        /// <summary>0 when standing or crouched and still, rising to 1 at full crouch-walking speed.</summary>
        internal static float CrouchWalkAmount(bool crouching, float speed, float crouchSpeed)
        {
            return crouching ? Mathf.Clamp01(speed / crouchSpeed) : 0f;
        }

        /// <summary>Moves toward the target at a rate that covers the whole 0 to 1 range in fadeTime seconds.</summary>
        internal static float FadeTowards(float current, float target, float fadeTime, float deltaTime)
        {
            if (fadeTime <= 0f) return target;
            return Mathf.MoveTowards(current, target, deltaTime / fadeTime);
        }
    }
}
