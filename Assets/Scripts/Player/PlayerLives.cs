using System;
using System.Collections.Generic;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Being caught costs a life and returns the player to their last checkpoint.
    /// Running out of lives refills them and sends the player back to the level's first checkpoint.
    /// The level sets its first checkpoint when it loads (<see cref="StartLevel"/>).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerLives : MonoBehaviour
    {
        [SerializeField] int maxLives = 3;

        PlayerController player;
        Checkpoint firstCheckpoint;
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
            Lives = maxLives;
        }

        /// <summary>A new level begins: full lives, standing on its first checkpoint, which counts as reached.</summary>
        public void StartLevel(Checkpoint first)
        {
            Lives = maxLives;
            reachedBefore.Clear();
            firstCheckpoint = first;
            current = first;
            if (first != null) reachedBefore.Add(first);
        }

        /// <summary>
        /// The player walked into the next area of a joined world. Lives carry on. Its first checkpoint is now where losing
        /// the last life sends them, and where a catch sends them once the area with their checkpoint has unloaded.
        /// </summary>
        public void EnterArea(Checkpoint first)
        {
            if (first != null) firstCheckpoint = first;
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

            // In a joined world, the area that held the checkpoint may have unloaded behind the player.
            if (current == null) current = firstCheckpoint;
            if (current != null) player.Respawn(current.Position);
            Caught?.Invoke(outOfLives);
        }
    }
}
