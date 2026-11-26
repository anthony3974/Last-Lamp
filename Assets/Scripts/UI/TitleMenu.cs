using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LastLamp
{
    /// <summary>
    /// The title screen's menu: New Game, Continue and Quit, over the Title scene's lamp-lit backdrop. The game opens on
    /// it, and the pause menu's Quit comes back to it. <see cref="LevelLoader"/> shows and hides it, behind the fade.
    /// Each button's On Click, set in the hierarchy, calls one of the public methods here.
    /// </summary>
    public class TitleMenu : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Everything shown on the title screen. Hidden while playing.")]
        [SerializeField] GameObject panel;
        [Tooltip("The button picked when the menu opens, so the keyboard works at once.")]
        [SerializeField] Selectable firstButton;

        MenuFocus focus;

        public bool IsOpen => panel.activeSelf;

        void Awake()
        {
            focus = new MenuFocus(firstButton);
            panel.SetActive(false);
        }

        void Update()
        {
            if (IsOpen) focus.Keep();
        }

        public void Show(bool show)
        {
            panel.SetActive(show);
            if (show) focus.SelectFirst();
            else focus.Select(null);
        }

        /// <summary>Starts the first level with fresh lives, as Play does with Main open.</summary>
        public void NewGame()
        {
            if (LevelLoader.Current != null) LevelLoader.Current.StartNewGame();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
    }
}
