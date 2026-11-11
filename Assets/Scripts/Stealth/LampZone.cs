using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace LastLamp
{
    public enum LightPoolShape
    {
        Circle,
        Square,
        Wedge,
    }

    /// <summary>
    /// Marks a pool of light as safe. Gameplay reads this and never the rendered light.
    /// Turning the object off removes its safe zone, but the Lit area in the baked NavMesh stays until a rebake.
    /// </summary>
    public class LampZone : MonoBehaviour
    {
        // The Wick's arms hang about 0.53 m out from its middle. Matches the enemies' NavMesh agent radius.
        const float EnemyBodyRadius = 0.55f;
        const float ReachAbove = 0.65f;

        static readonly List<LampZone> Active = new List<LampZone>();

        [SerializeField] LightPoolShape shape = LightPoolShape.Circle;
        [Tooltip("Circle: safe distance from the middle. Square: half the side. In metres. Rebake the NavMesh after changing it.")]
        [SerializeField] float radius = 2.6f;
        [Tooltip("How far below this object the pool reaches, in metres, so a lamp doesn't light the floor under a deck.")]
        [SerializeField] float reachBelow = 4.35f;

        [Header("Wedge only (metres, starting here and widening along local forward)")]
        [SerializeField] float length = 6f;
        [SerializeField] float nearWidth = 4f;
        [SerializeField] float farWidth = 7f;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public static bool AnyContains(Vector3 point)
        {
            foreach (var zone in Active)
            {
                if (zone.Contains(point)) return true;
            }
            return false;
        }

        internal static bool IsInside(Vector3 centre, float radius, Vector3 point)
        {
            float x = point.x - centre.x;
            float z = point.z - centre.z;
            return x * x + z * z <= radius * radius;
        }

        internal static bool IsInsideSquare(float localX, float localZ, float halfSide)
        {
            return Mathf.Abs(localX) <= halfSide && Mathf.Abs(localZ) <= halfSide;
        }

        internal static bool IsInsideWedge(float localX, float localZ, float length, float nearWidth, float farWidth)
        {
            if (localZ < 0f || localZ > length) return false;
            return Mathf.Abs(localX) <= Mathf.Lerp(nearWidth, farWidth, localZ / length) / 2f;
        }

        internal static bool IsWithinHeight(float zoneY, float reachBelow, float pointY)
        {
            return pointY >= zoneY - reachBelow && pointY <= zoneY + ReachAbove;
        }

        bool Contains(Vector3 point)
        {
            if (!IsWithinHeight(transform.position.y, reachBelow, point.y)) return false;

            Vector3 local = Quaternion.Inverse(transform.rotation) * (point - transform.position);
            switch (shape)
            {
                case LightPoolShape.Square: return IsInsideSquare(local.x, local.z, radius);
                case LightPoolShape.Wedge: return IsInsideWedge(local.x, local.z, length, nearWidth, farWidth);
                default: return IsInside(transform.position, radius, point);
            }
        }

        void OnValidate() => SyncNavMeshVolume();

        /// <summary>Keeps the Lit area enemies avoid big enough that their bodies stay out of the safe zone.</summary>
        public void SyncNavMeshVolume()
        {
            var volume = GetComponent<NavMeshModifierVolume>();
            if (volume == null) return;

            if (shape == LightPoolShape.Wedge)
            {
                volume.size = new Vector3(farWidth + EnemyBodyRadius * 2f, volume.size.y, length + EnemyBodyRadius);
                volume.center = new Vector3(0f, volume.center.y, (length + EnemyBodyRadius) / 2f);
                return;
            }

            float width = (radius + EnemyBodyRadius) * 2f;
            volume.size = new Vector3(width, volume.size.y, width);
            volume.center = new Vector3(0f, volume.center.y, 0f);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 position = transform.position;
            Vector3 ground = new Vector3(position.x, position.y - reachBelow, position.z);

            if (shape == LightPoolShape.Circle)
            {
                Gizmos.DrawWireSphere(ground, radius);
                return;
            }

            Gizmos.matrix = Matrix4x4.TRS(ground, transform.rotation, Vector3.one);
            if (shape == LightPoolShape.Square)
            {
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(radius * 2f, 0f, radius * 2f));
            }
            else
            {
                Vector3 nearLeft = new Vector3(-nearWidth / 2f, 0f, 0f);
                Vector3 nearRight = new Vector3(nearWidth / 2f, 0f, 0f);
                Vector3 farLeft = new Vector3(-farWidth / 2f, 0f, length);
                Vector3 farRight = new Vector3(farWidth / 2f, 0f, length);
                Gizmos.DrawLine(nearLeft, nearRight);
                Gizmos.DrawLine(nearRight, farRight);
                Gizmos.DrawLine(farRight, farLeft);
                Gizmos.DrawLine(farLeft, nearLeft);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
