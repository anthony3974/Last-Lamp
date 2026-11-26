using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Keeps a menu's keyboard pick. Clicking the dark around the buttons drops the selection, which would leave the
    /// keyboard with nothing to move from, so the last button picked comes back.
    /// </summary>
    public class MenuFocus
    {
        readonly Selectable firstButton;
        GameObject lastSelected;

        public MenuFocus(Selectable firstButton) => this.firstButton = firstButton;

        /// <summary>Picks the menu's first button, so the keyboard works at once.</summary>
        public void SelectFirst() => Select(firstButton.gameObject);

        public void Select(GameObject button)
        {
            lastSelected = button;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button);
        }

        /// <summary>Call every frame while the menu is open.</summary>
        public void Keep()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy) lastSelected = selected;
            else Select(lastSelected != null ? lastSelected : firstButton.gameObject);
        }
    }
}
