using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// How exposed the player should be, from 0 (safe) to 1 (fully exposed).
    /// A lamp pool is always 0. In the dark, moving raises it, crouching lowers it, and landing from a jump spikes it.
    /// </summary>
    internal static class VisibilityRules
    {
        const float StillExposure = 0.25f;
        const float CrouchMultiplier = 0.45f;
        const float LandingSpike = 0.85f;

        public static float Target(bool inLamp, float speed, float sprintSpeed, bool crouching)
        {
            if (inLamp) return 0f;

            float moving = sprintSpeed > 0f ? Mathf.Clamp01(speed / sprintSpeed) : 0f;
            float exposure = Mathf.Lerp(StillExposure, 1f, moving);
            return crouching ? exposure * CrouchMultiplier : exposure;
        }

        public static float Landing(bool inLamp, bool crouching)
        {
            if (inLamp) return 0f;
            return crouching ? LandingSpike * CrouchMultiplier : LandingSpike;
        }
    }
}
