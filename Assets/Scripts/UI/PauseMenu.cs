using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Esc pauses the game and opens the menu: Resume, Restart from checkpoint and Quit to the title screen. Esc again
    /// resumes.
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
        MenuFocus focus;

        public static bool IsPaused { get; private set; }

        void Awake()
        {
            pause = actions.FindAction(PauseAction, true);
            focus = new MenuFocus(firstButton);
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

            if (IsPaused) focus.Keep();
        }

        // Not while time is already frozen, by a level's end or a new level loading: those unfreeze it themselves. Not on
        // the title screen either, which is a menu already.
        static bool CanPause()
        {
            LevelLoader loader = LevelLoader.Current;
            return Time.timeScale > 0f && (loader == null || (!loader.IsSwitching && !loader.OnTitle));
        }

        public void Pause()
        {
            if (IsPaused) return;

            IsPaused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            player.SetControls(false);
            panel.SetActive(true);
            focus.SelectFirst();
        }

        public void Resume()
        {
            if (!IsPaused) return;

            Close();
            player.SetControls(true);
        }

        /// <summary>Back to the last checkpoint touched, with the same lives, and every monster starts over.</summary>
        public void RestartFromCheckpoint()
        {
            Resume();
            lives.RestartFromCheckpoint();
        }

        /// <summary>Back to the title screen. The controls stay off: the title screen is a menu.</summary>
        public void Quit()
        {
            if (!IsPaused || LevelLoader.Current == null) return;

            Close();
            LevelLoader.Current.ShowTitle();
        }

        void Close()
        {
            Unfreeze();
            panel.SetActive(false);
            focus.Select(null);
        }

        void Unfreeze()
        {
            IsPaused = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }
}
