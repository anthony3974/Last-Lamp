using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Tells the player what Interact will do: hide, come back out, or open or close a door.
    /// </summary>
    public class InteractPrompt : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerHiding hiding;
        [Tooltip("Optional: leave empty in a level without doors.")]
        [SerializeField] PlayerDoors doors;
        [SerializeField] Text label;

        [Header("Text")]
        [SerializeField] string hideText = "E  Hide";
        [SerializeField] string leaveText = "E  Leave";
        [SerializeField] string openText = "E  Open";
        [SerializeField] string closeText = "E  Close";

        void Update()
        {
            if (hiding.IsHidden || hiding.SpotInReach)
            {
                label.enabled = true;
                label.text = hiding.IsHidden ? leaveText : hideText;
            }
            else if (doors != null && doors.DoorInReach != null)
            {
                label.enabled = true;
                label.text = doors.DoorInReach.IsOpen ? closeText : openText;
            }
            else
            {
                label.enabled = false;
            }
        }
    }
}
