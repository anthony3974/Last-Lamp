using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// The player's current exposure. The on-screen cue reads it now, and enemies will read it in task 9.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVisibility : MonoBehaviour
    {
        [Tooltip("How fast exposure climbs, per second.")]
        [SerializeField] float riseRate = 1.5f;
        [Tooltip("How fast exposure drops, per second.")]
        [SerializeField] float fallRate = 2.5f;
        [Tooltip("How fast the spike from landing fades, per second.")]
        [SerializeField] float landingFadeRate = 1f;

        PlayerController player;
        float movementExposure;
        float landingExposure;

        public float Value => Mathf.Max(movementExposure, landingExposure);
        public bool IsInLamp { get; private set; }

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        void OnEnable() => player.Landed += OnLanded;
        void OnDisable() => player.Landed -= OnLanded;

        void Update()
        {
            if (player.IsHidden)
            {
                IsInLamp = false;
                movementExposure = 0f;
                landingExposure = 0f;
                return;
            }

            IsInLamp = LampZone.AnyContains(transform.position);

            float target = VisibilityRules.Target(IsInLamp, player.HorizontalSpeed, player.SprintSpeed, player.IsCrouching);
            float rate = target > movementExposure ? riseRate : fallRate;
            movementExposure = Mathf.MoveTowards(movementExposure, target, rate * Time.deltaTime);

            landingExposure = IsInLamp ? 0f : Mathf.MoveTowards(landingExposure, 0f, landingFadeRate * Time.deltaTime);
        }

        void OnLanded()
        {
            landingExposure = Mathf.Max(landingExposure, VisibilityRules.Landing(IsInLamp, player.IsCrouching));
        }
    }
}
