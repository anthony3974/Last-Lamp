using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// The parts of the HUD that things in a level use. The HUD is in the Main scene and a level can't link to another
    /// scene, so a level's end point and tutorial find them here.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public static Hud Current { get; private set; }

        [SerializeField] Text finishedMessage;
        [SerializeField] GameObject tutorialPanel;
        [SerializeField] Text tutorialLabel;
        [Tooltip("A black panel over the whole screen. Its Canvas Group fades it in while a new level loads.")]
        [SerializeField] CanvasGroup fade;

        public GameObject TutorialPanel => tutorialPanel;
        public Text TutorialLabel => tutorialLabel;
        public CanvasGroup Fade => fade;

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Shows the end of a level's message, such as "YOU MADE IT".</summary>
        public void ShowFinished(string text)
        {
            finishedMessage.text = text;
            finishedMessage.gameObject.SetActive(true);
        }

        public void HideFinished() => finishedMessage.gameObject.SetActive(false);
    }
}
