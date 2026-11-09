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

        public static float Landing(bool crouching)
        {
            return crouching ? LandingRadius * CrouchedLandingMultiplier : LandingRadius;
        }
    }
}
