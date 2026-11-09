using System.Collections.Generic;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// A place the player returns to after being caught. Walking onto it makes it the current one.
    /// </summary>
    public class Checkpoint : MonoBehaviour
    {
        static readonly List<Checkpoint> Active = new List<Checkpoint>();

        [Tooltip("How close the player must get to claim this checkpoint, along the ground, in metres.")]
        [SerializeField] float radius = 1.5f;

        public Vector3 Position => transform.position;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <returns>The checkpoint at this point, or null if there is none.</returns>
        public static Checkpoint At(Vector3 point)
        {
            foreach (var checkpoint in Active)
            {
                if (LampZone.IsInside(checkpoint.transform.position, checkpoint.radius, point)) return checkpoint;
            }
            return null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
