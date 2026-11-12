using System.Collections.Generic;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// A box where this area meets another, for areas joined into one world. While the player stands in it,
    /// <see cref="LevelLoader"/> loads the other area in the background alongside this one and keeps it loaded, so the
    /// player can walk, jump or look across with no break. Once they are out of every link to it, and not in it, it unloads.
    /// Make the box big enough that the other area has loaded before the player can see into it, and give the other area
    /// a link back that covers its own side of the crossing.
    /// </summary>
    public class AreaLink : MonoBehaviour
    {
        static readonly List<AreaLink> Active = new List<AreaLink>();

        [Tooltip("The scene of the area on the other side, by name, such as Warehouse_02. It must be in the build list.")]
        [SerializeField] string area = "";
        [Tooltip("Width (X), height (Y) and depth (Z) of the box around this object, in metres.")]
        [SerializeField] Vector3 size = new Vector3(12f, 8f, 12f);

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <summary>Adds the area of each link the point is in, once each, until the list holds max.</summary>
        public static void AreasAt(Vector3 point, List<string> areas, int max)
        {
            foreach (var link in Active)
            {
                if (areas.Count >= max) return;
                if (link.Contains(point) && !string.IsNullOrEmpty(link.area) && !areas.Contains(link.area)) areas.Add(link.area);
            }
        }

        bool Contains(Vector3 point)
        {
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
