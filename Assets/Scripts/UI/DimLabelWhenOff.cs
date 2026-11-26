using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// A button that can't be pressed greys out its label too, not just the bar behind it, such as Continue before there
    /// is a save to load.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class DimLabelWhenOff : MonoBehaviour
    {
        [SerializeField] Graphic label;
        [Tooltip("How much of the label shows while the button is off.")]
        [SerializeField, Range(0f, 1f)] float offAlpha = 0.3f;

        Selectable selectable;
        Color colour;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            colour = label.color;
        }

        void LateUpdate()
        {
            Color shown = colour;
            if (!selectable.IsInteractable()) shown.a *= offAlpha;
            if (label.color != shown) label.color = shown;
        }
    }
}
