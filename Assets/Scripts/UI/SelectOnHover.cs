using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Pointing at a button picks it, as the arrow keys do, so the mouse and the keyboard never light up two buttons at once.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    public class SelectOnHover : MonoBehaviour, IPointerEnterHandler
    {
        Selectable selectable;

        void Awake() => selectable = GetComponent<Selectable>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (selectable.IsInteractable() && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }
}
