using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// A dim light that points where the player looks. It is only for seeing by: stealth rules never read it.
    /// Goes out while the player is hidden, so it can't shine out of a hiding spot.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class PlayerFlashlight : MonoBehaviour
    {
        PlayerController player;
        Light beam;

        void Awake()
        {
            player = GetComponentInParent<PlayerController>();
            beam = GetComponent<Light>();
        }

        void Update()
        {
            beam.enabled = !player.IsHidden;
        }
    }
}
