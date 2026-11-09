using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// A loose object the player can bump. It flies off and makes noise when knocked or when it hits something hard.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
    public class KnockableObject : MonoBehaviour
    {
        [SerializeField] AudioClip clank;
        [SerializeField] float noiseRadius = 9f;
        [SerializeField] float kickMultiplier = 1.5f;
        [SerializeField] float kickLift = 0.3f;
        [SerializeField] float minPlayerSpeed = 0.5f;
        [SerializeField] float minImpactSpeed = 1.5f;
        [SerializeField] float noiseCooldown = 0.4f;

        // Objects settle onto the floor when the level starts, and that should not count as noise.
        const float SettleSeconds = 1f;

        Rigidbody body;
        AudioSource audioSource;
        float nextNoiseTime;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            audioSource = GetComponent<AudioSource>();
        }

        public void Knock(Vector3 direction, float playerSpeed)
        {
            if (playerSpeed < minPlayerSpeed) return;

            direction.y = 0f;
            body.linearVelocity = (direction.normalized + Vector3.up * kickLift) * (playerSpeed * kickMultiplier);
            MakeNoise();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (Time.timeSinceLevelLoad < SettleSeconds) return;
            if (collision.relativeVelocity.magnitude >= minImpactSpeed) MakeNoise();
        }

        void MakeNoise()
        {
            if (Time.time < nextNoiseTime) return;
            nextNoiseTime = Time.time + noiseCooldown;

            Noise.Emit(transform.position, noiseRadius);
            audioSource.PlayOneShot(clank);
        }
    }
}
