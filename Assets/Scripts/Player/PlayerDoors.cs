using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Pressing Interact near a door opens or closes it. A hiding spot in reach takes the press instead.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerDoors : MonoBehaviour
    {
        PlayerController player;

        /// <summary>The door Interact would open or close right now, or null.</summary>
        public Door DoorInReach { get; private set; }

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        void Update()
        {
            DoorInReach = null;
            if (player.IsHidden || HidingSpot.NearestInReach(transform.position) != null) return;

            Door door = Door.NearestInReach(transform.position);
            if (door == null || !door.CanToggle(transform.position)) return;

            DoorInReach = door;
            if (player.InteractPressed && !player.IsAirborne) door.Toggle(transform.position);
        }
    }
}
