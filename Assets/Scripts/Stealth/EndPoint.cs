using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastLamp
{
    /// <summary>
    /// Reaching this point ends the run: the game stops and Interact restarts the level.
    /// </summary>
    public class EndPoint : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] GameObject finishedMessage;
        [Tooltip("How close the player must get to finish, along the ground, in metres.")]
        [SerializeField] float radius = 2f;

        bool finished;

        void Update()
        {
            if (finished)
            {
                if (player.InteractPressed) Restart();
                return;
            }

            Vector3 playerPosition = player.transform.position;
            if (!LevelRules.SameLevel(transform.position, playerPosition)) return;
            if (!LampZone.IsInside(transform.position, radius, playerPosition)) return;

            finished = true;
            Time.timeScale = 0f;
            finishedMessage.SetActive(true);
        }

        void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.buildIndex);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
