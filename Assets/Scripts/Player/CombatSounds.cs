using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// The player's fight sounds: a swing on every punch, with a hit on top when it stuns a monster or a duller thud when
    /// the monster shrugs it off, and a whoosh as he rolls. Sound only: monsters don't hear any of it. Has its own
    /// Audio Source, so the footsteps' pitch changes don't bend these.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CombatSounds : MonoBehaviour
    {
        [Header("Punch")]
        [Tooltip("Every punch, hit or miss.")]
        [SerializeField] AudioClip swing;
        [SerializeField] float swingVolume = 0.6f;
        [Tooltip("On top of the swing, when the punch stuns a monster.")]
        [SerializeField] AudioClip hit;
        [SerializeField] float hitVolume = 1f;
        [Tooltip("On top of the swing, when it reaches only a monster stunned too recently.")]
        [SerializeField] AudioClip shruggedOff;
        [SerializeField] float shruggedOffVolume = 0.6f;

        [Header("Dodge")]
        [SerializeField] AudioClip dodge;
        [SerializeField] float dodgeVolume = 0.7f;

        [Tooltip("Each sound plays up to this much higher or lower, so repeats don't sound identical.")]
        [SerializeField] float pitchVariation = 0.05f;

        PlayerController player;
        PlayerCombat combat;
        AudioSource voice;

        void Awake()
        {
            player = GetComponentInParent<PlayerController>();
            combat = GetComponentInParent<PlayerCombat>();
            voice = GetComponent<AudioSource>();
        }

        void OnEnable()
        {
            combat.Punched += OnPunched;
            player.Dodged += OnDodged;
        }

        void OnDisable()
        {
            combat.Punched -= OnPunched;
            player.Dodged -= OnDodged;
        }

        void OnPunched()
        {
            voice.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            Play(swing, swingVolume);
            if (combat.LastPunch == PunchResult.Stunned) Play(hit, hitVolume);
            else if (combat.LastPunch == PunchResult.ShruggedOff) Play(shruggedOff, shruggedOffVolume);
        }

        void OnDodged()
        {
            voice.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
            Play(dodge, dodgeVolume);
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip != null) voice.PlayOneShot(clip, volume);
        }
    }
}
