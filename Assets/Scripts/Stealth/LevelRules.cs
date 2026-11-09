using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Tells apart things on different floors, so ground-distance checks don't reach through a deck.
    /// </summary>
    internal static class LevelRules
    {
        const float MaxHeightDifference = 2.5f;

        public static bool SameLevel(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.y - b.y) <= MaxHeightDifference;
        }
    }
}
