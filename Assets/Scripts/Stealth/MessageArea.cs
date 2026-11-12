using System.Collections.Generic;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// An area on the ground that shows a message on the HUD while the player stands in it, such as
    /// "This door is blocked." in front of a blocked door. <see cref="GameMessage"/> shows it.
    /// </summary>
    public class MessageArea : MonoBehaviour
    {
        static readonly List<MessageArea> Active = new List<MessageArea>();

        [TextArea(1, 3)]
        [SerializeField] string text = "This door is blocked.";
        [Tooltip("Width (X) and depth (Z) of the area around this object, in metres.")]
        [SerializeField] Vector2 areaSize = new Vector2(4f, 4f);

        public string Text => text;

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <returns>The area at this point, or null if there is none.</returns>
        public static MessageArea At(Vector3 point)
        {
            foreach (var area in Active)
            {
                if (area.Contains(point)) return area;
            }
            return null;
        }

        bool Contains(Vector3 point)
        {
            if (!LevelRules.SameLevel(transform.position, point)) return false;
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f && Mathf.Abs(local.z) <= areaSize.y * 0.5f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(areaSize.x, 0f, areaSize.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
