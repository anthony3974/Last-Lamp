using System;
using System.Collections.Generic;
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
        readonly HashSet<Checkpoint> reachedBefore = new HashSet<Checkpoint>();

        /// <summary>Raised after each catch. True means that catch used up the last life.</summary>
        public event Action<bool> Caught;

        /// <summary>Raised the first time the player reaches each checkpoint, not counting the first one.</summary>
        public event Action<Checkpoint> CheckpointReached;

        public int Lives { get; private set; }

        void Awake()
        {
            player = GetComponent<PlayerController>();
            current = firstCheckpoint;
            Lives = maxLives;
            if (firstCheckpoint != null) reachedBefore.Add(firstCheckpoint);
        }

        void Update()
        {
            if (player.IsHidden) return;

            Checkpoint reached = Checkpoint.At(transform.position);
            if (reached == null || reached == current) return;

            current = reached;
            if (reachedBefore.Add(reached)) CheckpointReached?.Invoke(reached);
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
