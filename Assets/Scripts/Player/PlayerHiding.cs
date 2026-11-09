using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Pressing Interact near a hiding spot goes in, and pressing it again comes back out.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerHiding : MonoBehaviour
    {
        PlayerController player;
        HidingSpot occupied;

        public bool IsHidden => player.IsHidden;
        public bool SpotInReach { get; private set; }

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        void Update()
        {
            if (player.IsHidden)
            {
                if (player.InteractPressed) Leave();
                return;
            }

            HidingSpot spot = HidingSpot.NearestInReach(transform.position);
            SpotInReach = spot != null;

            if (spot != null && player.InteractPressed && !player.IsAirborne) Enter(spot);
        }

        void Enter(HidingSpot spot)
        {
            occupied = spot;
            player.EnterHiding(spot.HidePosition);
        }

        void Leave()
        {
            player.LeaveHiding(occupied.ExitPosition);
            occupied = null;
        }
    }
}
