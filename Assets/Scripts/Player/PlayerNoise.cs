using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// The player's footsteps and landings, as both sound and noise events, plus knocking loose objects.
    /// The noise enemies hear runs on the stride timer, so stealth balance doesn't depend on the animation. The footstep
    /// sound comes from the animation instead, so it lands with his feet.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(AudioSource))]
    public class PlayerNoise : MonoBehaviour
    {
        const float MinStepSpeed = 0.5f;

        [Header("Sounds")]
        [Tooltip("Played in turn, one per step.")]
        [SerializeField] AudioClip[] footsteps;
        [SerializeField] AudioClip landing;
        [SerializeField] float pitchVariation = 0.1f;

        [Header("Steps")]
        [SerializeField] float walkStride = 1.7f;
        [SerializeField] float sprintStride = 2.4f;
        [SerializeField] float walkVolume = 0.35f;
        [SerializeField] float sprintVolume = 0.8f;

        [Header("Landing")]
        [SerializeField] float landingVolume = 0.9f;
        [SerializeField] float crouchedLandingVolume = 0.3f;

        PlayerController player;
        AudioSource audioSource;
        float strideProgress;
        int nextFootstep;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            audioSource = GetComponent<AudioSource>();
        }

        void OnEnable() => player.Landed += OnLanded;
        void OnDisable() => player.Landed -= OnLanded;

        void Update()
        {
            float speed = player.HorizontalSpeed;
            if (player.IsAirborne || player.IsCrouching || speed < MinStepSpeed)
            {
                strideProgress = 0f;
                return;
            }

            strideProgress += speed * Time.deltaTime;
            float stride = player.IsSprinting ? sprintStride : walkStride;
            if (strideProgress < stride) return;

            strideProgress -= stride;
            TakeStep();
        }

        void TakeStep()
        {
            bool inLamp = LampZone.AnyContains(transform.position);
            Noise.Emit(transform.position, NoiseRules.Footstep(player.IsSprinting, player.IsCrouching, inLamp));
        }

        /// <summary>Called by the animation as a foot lands.</summary>
        public void PlayFootstep()
        {
            if (footsteps.Length == 0) return;

            Play(footsteps[nextFootstep], player.IsSprinting ? sprintVolume : walkVolume);
            nextFootstep = (nextFootstep + 1) % footsteps.Length;
        }

        void OnLanded()
        {
            bool inLamp = LampZone.AnyContains(transform.position);
            Noise.Emit(transform.position, NoiseRules.Landing(player.IsCrouching, inLamp));
            Play(landing, player.IsCrouching ? crouchedLandingVolume : landingVolume);
        }

        void Play(AudioClip clip, float volume)
        {
            audioSource.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            audioSource.PlayOneShot(clip, volume);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var knockable = hit.collider.GetComponent<KnockableObject>();
            if (knockable != null) knockable.Knock(hit.moveDirection, player.HorizontalSpeed);
        }
    }
}
