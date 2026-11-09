using System;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Being caught costs a life and returns the player to their last checkpoint.
    /// Running out of lives refills them and sends the player back to the first checkpoint.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerLives : MonoBehaviour
    {
        [SerializeField] Checkpoint firstCheckpoint;
        [SerializeField] int maxLives = 3;

        PlayerController player;
        Checkpoint current;

        /// <summary>Raised after each catch. True means that catch used up the last life.</summary>
        public event Action<bool> Caught;

        public int Lives { get; private set; }

        void Awake()
        {
            player = GetComponent<PlayerController>();
            current = firstCheckpoint;
            Lives = maxLives;
        }

        void Update()
        {
            if (player.IsHidden) return;

            Checkpoint reached = Checkpoint.At(transform.position);
            if (reached != null) current = reached;
        }

        public void Catch()
        {
            Lives--;
            bool outOfLives = Lives <= 0;

            if (outOfLives)
            {
                Lives = maxLives;
                current = firstCheckpoint;
            }

            player.Respawn(current.Position);
            Caught?.Invoke(outOfLives);
        }
    }
}
