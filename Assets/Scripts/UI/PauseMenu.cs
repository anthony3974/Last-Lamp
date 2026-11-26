using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LastLamp
{
    /// <summary>
    /// Esc pauses the game and opens the menu: Resume, Restart from checkpoint and Quit. Esc again resumes.
    ///
    /// While paused, time stands still (monsters, their catch timers, doors, animation), every sound is held where it
    /// was, and the player's controls are off, so the mouse is free for the menu. Each button's On Click, set in the
    /// hierarchy, calls one of the public methods here.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        const string PauseAction = "UI/Pause";

        [Header("References")]
        [Tooltip("The input actions. Esc is UI/Pause.")]
        [SerializeField] InputActionAsset actions;
        [SerializeField] PlayerController player;
        [SerializeField] PlayerLives lives;
        [Tooltip("Everything shown while paused. Hidden while playing.")]
        [SerializeField] GameObject panel;
        [Tooltip("The button picked when the menu opens, so the keyboard works at once.")]
        [SerializeField] Selectable firstButton;

        InputAction pause;
        GameObject lastSelected;

        public static bool IsPaused { get; private set; }

        void Awake()
        {
            pause = actions.FindAction(PauseAction, true);
            panel.SetActive(false);
        }

        void OnEnable() => pause.Enable();

        void OnDisable()
        {
            pause.Disable();
            // Leaving play mode, or Main closing, mustn't leave the game frozen and silent.
            if (IsPaused) Unfreeze();
        }

        void Update()
        {
            if (pause.WasPressedThisFrame())
            {
                if (IsPaused) Resume();
                else if (CanPause()) Pause();
            }

            if (IsPaused) KeepSelection();
        }

        // Not while time is already frozen, by a level's end or a new level loading: those unfreeze it themselves.
        static bool CanPause() => Time.timeScale > 0f && (LevelLoader.Current == null || !LevelLoader.Current.IsSwitching);

        public void Pause()
        {
            if (IsPaused) return;

            IsPaused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            player.SetControls(false);
            panel.SetActive(true);
            Select(firstButton.gameObject);
        }

        public void Resume()
        {
            if (!IsPaused) return;

            Unfreeze();
            panel.SetActive(false);
            Select(null);
            player.SetControls(true);
        }

        /// <summary>Back to the last checkpoint touched, with the same lives, and every monster starts over.</summary>
        public void RestartFromCheckpoint()
        {
            Resume();
            lives.RestartFromCheckpoint();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }

        void Unfreeze()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // Clicking the dark around the buttons drops the selection, which would leave the keyboard with nothing to move
        // from, so the last button picked comes back.
        void KeepSelection()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            GameObject selected = events.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy) lastSelected = selected;
            else Select(lastSelected != null ? lastSelected : firstButton.gameObject);
        }

        void Select(GameObject button)
        {
            lastSelected = button;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button);
        }
    }
}
