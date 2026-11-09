using UnityEngine;

namespace LastLamp
{
    internal static class CombatRules
    {
        /// <summary>Whether a punch thrown from one point, in a direction, reaches a target. Height is ignored.</summary>
        public static bool InPunchArc(Vector3 from, Vector3 direction, Vector3 target, float reach, float halfAngle)
        {
            Vector3 offset = target - from;
            offset.y = 0f;
            direction.y = 0f;

            if (offset.magnitude > reach) return false;
            return offset.sqrMagnitude < 0.0001f || Vector3.Angle(direction, offset) <= halfAngle;
        }
    }
}
