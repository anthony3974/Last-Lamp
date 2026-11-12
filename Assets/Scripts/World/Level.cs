using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LastLamp
{
    /// <summary>
    /// The root of a level scene, such as Corridor_01 or Warehouse_01: where the player starts and the checkpoint they
    /// start on. A level holds only its own place (buildings, lamps, monsters, checkpoints, its NavMesh); the player,
    /// camera, HUD and the night's light are in the Main scene, which <see cref="LevelLoader"/> loads levels alongside.
    /// It must sit at the root of the scene.
    /// </summary>
    public class Level : MonoBehaviour
    {
        static readonly List<Level> Loaded = new List<Level>();

        [Tooltip("Where the player starts when this level loads as a new level, or is played on its own. They face along its blue arrow.")]
        [SerializeField] Transform playerStart;
        [Tooltip("The checkpoint the player starts on. Losing the last life sends them back to it.")]
        [SerializeField] Checkpoint firstCheckpoint;

        [Header("Joined areas")]
        [Tooltip("The space this area covers, in world coordinates. Only used when areas are joined into one world with " +
                 "Area Links: the player is in this area while inside it. Where two areas meet, let their boxes overlap a little.")]
        [SerializeField] Bounds bounds;

        public string SceneName => gameObject.scene.name;
        public Checkpoint FirstCheckpoint => firstCheckpoint;
        public Vector3 StartPosition => playerStart != null ? playerStart.position : transform.position;
        public Quaternion StartRotation => playerStart != null ? playerStart.rotation : transform.rotation;

        void OnEnable() => Loaded.Add(this);
        void OnDisable() => Loaded.Remove(this);

        public bool Contains(Vector3 point) => bounds.size != Vector3.zero && bounds.Contains(point);

        /// <returns>The level in this scene, or null if it has none.</returns>
        public static Level In(Scene scene)
        {
            foreach (var level in Loaded)
            {
                if (level.gameObject.scene == scene) return level;
            }
            return null;
        }

        /// <returns>
        /// The loaded area the point is in. The current one wins while the point is still inside it, so the overlap where
        /// two areas meet doesn't flip between them. If no area holds the point, the current one.
        /// </returns>
        public static Level At(Vector3 point, Level current)
        {
            if (current != null && current.Contains(point)) return current;
            foreach (var level in Loaded)
            {
                if (level.Contains(point)) return level;
            }
            return current;
        }

        void OnDrawGizmosSelected()
        {
            if (bounds.size != Vector3.zero)
            {
                Gizmos.color = new Color(0.5f, 0.9f, 0.5f);
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }

            if (playerStart == null) return;
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(playerStart.position + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawRay(playerStart.position + Vector3.up * 0.9f, playerStart.forward);
        }
    }
}
