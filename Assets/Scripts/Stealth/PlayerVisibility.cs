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
        [Tooltip("Seconds in the air before touching down counts as a landing.")]
        [SerializeField] float minAirTime = 0.15f;

        PlayerController player;
        float movementExposure;
        float landingExposure;
        float airTime;

        public float Value => Mathf.Max(movementExposure, landingExposure);
        public bool IsInLamp { get; private set; }

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        void Update()
        {
            IsInLamp = LampZone.AnyContains(transform.position);

            float target = VisibilityRules.Target(IsInLamp, player.HorizontalSpeed, player.SprintSpeed, player.IsCrouching);
            float rate = target > movementExposure ? riseRate : fallRate;
            movementExposure = Mathf.MoveTowards(movementExposure, target, rate * Time.deltaTime);

            landingExposure = IsInLamp ? 0f : Mathf.MoveTowards(landingExposure, 0f, landingFadeRate * Time.deltaTime);
            TrackLanding();
        }

        void TrackLanding()
        {
            if (player.IsAirborne)
            {
                airTime += Time.deltaTime;
                return;
            }

            if (airTime >= minAirTime)
                landingExposure = Mathf.Max(landingExposure, VisibilityRules.Landing(IsInLamp, player.IsCrouching));
            airTime = 0f;
        }
    }
}
