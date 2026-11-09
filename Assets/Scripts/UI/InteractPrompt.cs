using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Tells the player when Interact will hide them or bring them back out.
    /// </summary>
    public class InteractPrompt : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerHiding hiding;
        [SerializeField] Text label;

        [Header("Text")]
        [SerializeField] string hideText = "E  Hide";
        [SerializeField] string leaveText = "E  Leave";

        void Update()
        {
            label.enabled = hiding.IsHidden || hiding.SpotInReach;
            label.text = hiding.IsHidden ? leaveText : hideText;
        }
    }
}
