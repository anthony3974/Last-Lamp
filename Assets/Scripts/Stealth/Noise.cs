using System;
using UnityEngine;

namespace LastLamp
{
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Radius;

        public NoiseEvent(Vector3 position, float radius)
        {
            Position = position;
            Radius = radius;
        }
    }

    /// <summary>
    /// Anything that makes a sound the enemies could hear calls Emit. Enemies listen to Emitted in task 9.
    /// </summary>
    public static class Noise
    {
        public static event Action<NoiseEvent> Emitted;

        public static void Emit(Vector3 position, float radius)
        {
            if (radius <= 0f) return;
            Emitted?.Invoke(new NoiseEvent(position, radius));
        }
    }
}
