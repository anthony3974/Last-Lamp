using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Short game messages in a panel on the HUD: "Checkpoint reached" for a moment the first time the player reaches
    /// a checkpoint, and a <see cref="MessageArea"/>'s text for as long as the player stands in it. Fades in and out.
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

        [Header("Height, from the top of the screen")]
        [Tooltip("Where the panel sits in a level with a tutorial: under the tutorial panel.")]
        [SerializeField] float tutorialLevelY = -230f;
        [Tooltip("Where the panel sits in a level without one: at the top.")]
        [SerializeField] float otherLevelY = -110f;

        RectTransform panelRect;
        string timedText;
        float timedLeft;

        void Awake() => panelRect = (RectTransform)panel.transform;

        void OnEnable() => lives.CheckpointReached += OnCheckpointReached;
        void OnDisable() => lives.CheckpointReached -= OnCheckpointReached;

        void Start() => panel.alpha = 0f;

        void OnCheckpointReached(Checkpoint checkpoint) => Show(checkpointText, checkpointSeconds);

        /// <summary>Shows a message for a few seconds. It takes over from an area's message while it lasts.</summary>
        public void Show(string text, float seconds)
        {
            timedText = text;
            timedLeft = seconds;
        }

        void Update()
        {
            timedLeft = Mathf.Max(0f, timedLeft - Time.deltaTime);

            string text = timedLeft > 0f ? timedText : AreaText();
            if (text != null) label.text = text;

            float target = text != null ? 1f : 0f;
            float step = fadeSeconds > 0f ? Time.deltaTime / fadeSeconds : 1f;
            panel.alpha = Mathf.MoveTowards(panel.alpha, target, step);

            Vector2 position = panelRect.anchoredPosition;
            position.y = Tutorial.InLevel ? tutorialLevelY : otherLevelY;
            panelRect.anchoredPosition = position;
        }

        string AreaText()
        {
            if (player.IsHidden) return null;
            MessageArea area = MessageArea.At(player.transform.position);
            return area != null ? area.Text : null;
        }
    }
}
