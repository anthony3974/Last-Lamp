using UnityEngine;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Shows how many lives are left, and a short message each time the player is caught.
    /// </summary>
    public class LivesCue : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] PlayerLives lives;
        [SerializeField] Text livesLabel;
        [SerializeField] Text caughtLabel;

        [Header("Message")]
        [SerializeField] string caughtText = "CAUGHT";
        [SerializeField] string outOfLivesText = "OUT OF LIVES";
        [SerializeField] float messageSeconds = 1.5f;

        float messageTimeLeft;

        void OnEnable() => lives.Caught += ShowMessage;
        void OnDisable() => lives.Caught -= ShowMessage;

        void Update()
        {
            livesLabel.text = $"Lives {lives.Lives}";
            messageTimeLeft = Mathf.Max(0f, messageTimeLeft - Time.deltaTime);
            caughtLabel.enabled = messageTimeLeft > 0f;
        }

        void ShowMessage(bool outOfLives)
        {
            caughtLabel.text = outOfLives ? outOfLivesText : caughtText;
            messageTimeLeft = messageSeconds;
        }
    }
}
