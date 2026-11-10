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

        Animator animator;
        PlayerController player;
        PlayerCombat combat;
        PlayerNoise noise;
        bool wasDodging;

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
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0) return;

            float drop = HipDrop(player.IsCrouching, animator.GetFloat(Speed), player.CrouchSpeed, crouchWalkDrop);
            if (drop <= 0f) return;

            // Lower the body while the feet stay where the clip put them, so the knees bend instead of the feet sinking.
            PinFoot(AvatarIKGoal.LeftFoot);
            PinFoot(AvatarIKGoal.RightFoot);
            animator.bodyPosition += Vector3.down * drop;
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
            return crouching ? maxDrop * Mathf.Clamp01(speed / crouchSpeed) : 0f;
        }
    }
}
