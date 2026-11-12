using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Short game messages in a panel on the HUD: "Checkpoint reached" for a moment the first time the player reaches
    /// a checkpoint. Fades in and out.
    /// </summary>
    public class GameMessage : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerController player;
        [SerializeField] PlayerLives lives;
        [Tooltip("The panel to show and hide. Needs a Canvas Group, which does the fading.")]
        [SerializeField] CanvasGroup panel;
        [SerializeField] Text label;

        [Header("Checkpoint")]
        [SerializeField] string checkpointText = "Checkpoint reached";
        [SerializeField] float checkpointSeconds = 2.5f;

        [Header("Fade (seconds)")]
        [SerializeField] float fadeSeconds = 0.3f;

        string timedText;
        float timedLeft;

        void OnEnable() => lives.CheckpointReached += OnCheckpointReached;
        void OnDisable() => lives.CheckpointReached -= OnCheckpointReached;

        void Start() => panel.alpha = 0f;

        void OnCheckpointReached(Checkpoint checkpoint) => Show(checkpointText, checkpointSeconds);

        /// <summary>Shows a message for a few seconds.</summary>
        public void Show(string text, float seconds)
        {
            timedText = text;
            timedLeft = seconds;
        }

        void Update()
        {
            timedLeft = Mathf.Max(0f, timedLeft - Time.deltaTime);

            string text = timedLeft > 0f ? timedText : null;
            if (text != null) label.text = text;

            float target = text != null ? 1f : 0f;
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            panel.alpha = Mathf.MoveTowards(panel.alpha, target, step);
        }
    }
}
