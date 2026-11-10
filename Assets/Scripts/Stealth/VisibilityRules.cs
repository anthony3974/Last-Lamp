using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// How exposed the player should be, from 0 (safe) to 1 (fully exposed).
    /// A lamp pool is 0 unless the bright flashlight is on, which adds exposure anywhere. In the dark, moving raises it, crouching lowers it, and landing from a jump spikes it.
    /// </summary>
    internal static class VisibilityRules
    {
        const float StillExposure = 0.25f;
        const float CrouchMultiplier = 0.45f;
        const float LandingSpike = 0.85f;
        const float BrightLightExposure = 0.5f;

        public static float Target(bool inLamp, float speed, float sprintSpeed, bool crouching, bool brightLight = false)
        {
            float exposure = 0f;
            if (!inLamp)
            {
                float moving = sprintSpeed > 0f ? Mathf.Clamp01(speed / sprintSpeed) : 0f;
                exposure = Mathf.Lerp(StillExposure, 1f, moving);
                if (crouching) exposure *= CrouchMultiplier;
            }

            // The bright beam gives the player away even inside a lamp pool.
            return brightLight ? Mathf.Clamp01(exposure + BrightLightExposure) : exposure;
        }

        public static float Landing(bool inLamp, bool crouching)
        {
            if (inLamp) return 0f;
            return crouching ? LandingSpike * CrouchMultiplier : LandingSpike;
        }
    }
}
