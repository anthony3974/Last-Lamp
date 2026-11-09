using System.Collections.Generic;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Draws a fading ring for each recent noise. Turn on Gizmos in the Game view to see it during play.
    /// </summary>
    public class NoiseDebugView : MonoBehaviour
    {
        const int RingSegments = 48;

        [SerializeField] float showSeconds = 1.5f;

        readonly List<(NoiseEvent noise, float time)> recent = new List<(NoiseEvent, float)>();

        void OnEnable() => Noise.Emitted += Remember;
        void OnDisable() => Noise.Emitted -= Remember;

        void Remember(NoiseEvent noise) => recent.Add((noise, Time.time));

        void OnDrawGizmos()
        {
            recent.RemoveAll(entry => Time.time - entry.time > showSeconds);

            foreach (var (noise, time) in recent)
            {
                float remaining = 1f - (Time.time - time) / showSeconds;
                Gizmos.color = new Color(1f, 0.3f, 0.2f, remaining);
                DrawRing(noise.Position, noise.Radius);
            }
        }

        static void DrawRing(Vector3 centre, float radius)
        {
            centre.y = 0.05f;
            Vector3 previous = centre + Vector3.right * radius;

            for (int i = 1; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
