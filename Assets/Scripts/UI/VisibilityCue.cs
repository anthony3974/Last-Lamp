using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Shows how exposed the player is: a bar that fills and turns red, and a square that lights up in a lamp pool.
    /// </summary>
    public class VisibilityCue : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerVisibility visibility;
        [SerializeField] Image fill;
        [SerializeField] Image lampIndicator;

        [Header("Colours")]
        [SerializeField] Color calmColour = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] Color exposedColour = new Color(0.85f, 0.1f, 0.1f);
        [SerializeField] Color lampOnColour = new Color(1f, 0.9f, 0.6f);
        [SerializeField] Color lampOffColour = new Color(0.15f, 0.15f, 0.15f);

        void Update()
        {
            float value = visibility.Value;
            fill.rectTransform.anchorMax = new Vector2(value, 1f);
            fill.color = Color.Lerp(calmColour, exposedColour, value);
            lampIndicator.color = visibility.IsInLamp ? lampOnColour : lampOffColour;
        }
    }
}
