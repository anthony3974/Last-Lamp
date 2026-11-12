namespace LastLamp
{
    /// <summary>
    /// How far the player's sounds carry, in metres. A radius of 0 is silent.
    /// </summary>
    internal static class NoiseRules
    {
        const float WalkRadius = 6f;
        const float SprintRadius = 14f;
        const float LandingRadius = 10f;
        const float CrouchedLandingMultiplier = 0.5f;

        public static float Footstep(bool sprinting, bool crouching)
        {
            if (crouching) return 0f;
            return sprinting ? SprintRadius : WalkRadius;
        }

        /// <summary>Landing inside a lamp pool is silent to enemies, so a pool stays safe to jump in.</summary>
        public static float Landing(bool crouching, bool inLamp = false)
        {
            if (inLamp) return 0f;
            return crouching ? LandingRadius * CrouchedLandingMultiplier : LandingRadius;
        }
    }
}
