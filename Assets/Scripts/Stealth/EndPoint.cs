using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Reaching this point ends the level: the game freezes on the finished message. With a next level set, it loads
    /// that level after a short hold. Without one, Interact restarts this level.
    /// </summary>
    public class EndPoint : MonoBehaviour
    {
        [Tooltip("The message the game freezes on, shown in the middle of the HUD.")]
        [TextArea(1, 4)]
        [SerializeField] string finishedText = "YOU MADE IT";
        [Tooltip("How close the player must get to finish, along the ground, in metres.")]
        [SerializeField] float radius = 2f;

        [Header("Next level")]
        [Tooltip("The scene to load after the hold, by name, such as Warehouse_01. It must be in the build list. Leave empty to restart this level with Interact instead.")]
        [SerializeField] string nextLevel = "";
        [Tooltip("How long the game stays frozen on the finished message before the next level loads, in real seconds.")]
        [SerializeField] float holdSeconds = 2f;

        PlayerController player;
        bool finished;
        bool leaving;
        float heldFor;

        void Awake() => player = PlayerController.Current;

        void Update()
        {
            if (leaving) return;

            if (finished)
            {
                if (string.IsNullOrEmpty(nextLevel))
                {
                    if (player.InteractPressed) Leave(gameObject.scene.name);
                    return;
                }

                // The game is frozen (time scale 0), so the hold counts real time.
                heldFor += Time.unscaledDeltaTime;
                if (heldFor >= holdSeconds) Leave(nextLevel);
                return;
            }

            Vector3 playerPosition = player.transform.position;
            if (!LevelRules.SameLevel(transform.position, playerPosition)) return;
            if (!LampZone.IsInside(transform.position, radius, playerPosition)) return;

            finished = true;
            Time.timeScale = 0f;
            Hud.Current.ShowFinished(finishedText);
        }

        // The Level Loader fades out, swaps this level for the next (or a fresh copy of this one) and unfreezes the game.
        void Leave(string level)
        {
            leaving = true;
            LevelLoader.Current.LoadLevel(level);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
