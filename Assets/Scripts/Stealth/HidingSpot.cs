using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastLamp
{
    /// <summary>
    /// Somewhere the player can hide. The player waits at this object's position and comes back out at the exit point.
    /// </summary>
    public class HidingSpot : MonoBehaviour
    {
        static readonly List<HidingSpot> Active = new List<HidingSpot>();

        [SerializeField] Transform exitPoint;
        [Tooltip("How close the player must be to use this spot, along the ground, in metres.")]
        [SerializeField] float reach = 1.8f;

        Renderer[] renderers;
        ShadowCastingMode[] normalShadows;

        public Vector3 HidePosition => transform.position;
        public Vector3 ExitPosition => exitPoint.position;

        void OnEnable() => Active.Add(this);

        void OnDisable()
        {
            Active.Remove(this);
            SetOccupied(false);
        }

        /// <summary>
        /// While the player is inside, the spot draws only its shadow, so the follow camera, which can end up inside
        /// or behind it, sees the room instead of the inside of the crate. Monsters don't use rendering, so this
        /// changes nothing for them.
        /// </summary>
        public void SetOccupied(bool occupied)
        {
            if (renderers == null)
            {
                if (!occupied) return;
                renderers = GetComponentsInChildren<Renderer>(true);
                normalShadows = new ShadowCastingMode[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) normalShadows[i] = renderers[i].shadowCastingMode;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].shadowCastingMode = occupied ? ShadowCastingMode.ShadowsOnly : normalShadows[i];
            }
        }

        /// <returns>The closest spot the point can reach, or null if there is none.</returns>
        public static HidingSpot NearestInReach(Vector3 point)
        {
            HidingSpot nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var spot in Active)
            {
                if (!LevelRules.SameLevel(spot.transform.position, point)) continue;

                Vector3 offset = spot.transform.position - point;
                offset.y = 0f;
                float distance = offset.magnitude;

                if (distance <= spot.reach && distance < nearestDistance)
                {
                    nearest = spot;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, reach);
        }
    }
}
