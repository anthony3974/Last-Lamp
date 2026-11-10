using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// A dim light that points where the player looks. Holding F switches it to a brighter beam that lights more
    /// of the room but makes the player easier to spot (PlayerVisibility reads <see cref="IsBright"/>).
    /// Goes out while the player is hidden, so it can't shine out of a hiding spot.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class PlayerFlashlight : MonoBehaviour
    {
        [Tooltip("How many times brighter the beam is in bright mode.")]
        [SerializeField] float brightIntensityMultiplier = 2.5f;
        [Tooltip("How many times further the beam reaches in bright mode.")]
        [SerializeField] float brightRangeMultiplier = 1.5f;

        PlayerController player;
        Light beam;
        float dimIntensity;
        float dimRange;

        /// <summary>True while F is held and the player is out in the open.</summary>
        public bool IsBright { get; private set; }

        void Awake()
        {
            player = GetComponentInParent<PlayerController>();
            beam = GetComponent<Light>();
            dimIntensity = beam.intensity;
            dimRange = beam.range;
        }

        void Update()
        {
            IsBright = player.FlashlightHeld && !player.IsHidden;

            beam.enabled = !player.IsHidden;
            beam.intensity = IsBright ? dimIntensity * brightIntensityMultiplier : dimIntensity;
            beam.range = IsBright ? dimRange * brightRangeMultiplier : dimRange;
        }
    }
}
