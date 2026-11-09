using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Marks the pool under a lamp as safe. Gameplay reads this and never the rendered light.
    /// Turning the lamp object off removes its safe zone, but the Lit area in the baked NavMesh stays until a rebake.
    /// </summary>
    public class LampZone : MonoBehaviour
    {
        const float EnemyBodyRadius = 0.45f;

        static readonly List<LampZone> Active = new List<LampZone>();

        [Tooltip("Safe distance from the lamp along the ground, in metres. Rebake the NavMesh after changing it.")]
        [SerializeField] float radius = 2.6f;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public static bool AnyContains(Vector3 point)
        {
            foreach (var zone in Active)
            {
                if (IsInside(zone.transform.position, zone.radius, point)) return true;
            }
            return false;
        }

        internal static bool IsInside(Vector3 centre, float radius, Vector3 point)
        {
            float x = point.x - centre.x;
            float z = point.z - centre.z;
            return x * x + z * z <= radius * radius;
        }

        // Keeps the Lit area enemies avoid big enough that their bodies stay out of the safe zone.
        void OnValidate()
        {
            var volume = GetComponent<NavMeshModifierVolume>();
            if (volume == null) return;

            float width = (radius + EnemyBodyRadius) * 2f;
            volume.size = new Vector3(width, volume.size.y, width);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 position = transform.position;
            Gizmos.DrawWireSphere(new Vector3(position.x, 0f, position.z), radius);
        }
    }
}
