using UnityEngine;

namespace LastLamp
{
    internal enum DetectionStage
    {
        Unaware,
        Suspicious,
        Spotted,
    }

    /// <summary>
    /// How close an enemy is to noticing the player. Reaching Spotted always takes a warning first:
    /// the meter moves one stage per tick, and must stay Suspicious for WarningSeconds.
    /// </summary>
    internal sealed class DetectionMeter
    {
        public const float SuspicionThreshold = 0.25f;
        public const float WarningSeconds = 0.75f;
        const float GainPerSecond = 1.4f;
        const float LossPerSecond = 0.4f;
        const float GainAtMaxRange = 0.3f;

        float secondsSuspicious;

        public float Awareness { get; private set; }
        public DetectionStage Stage { get; private set; } = DetectionStage.Unaware;

        /// <param name="exposure">The player's visibility, 0 to 1.</param>
        /// <param name="distanceFraction">0 next to the enemy, 1 at the limit of its sight.</param>
        public void Tick(bool seen, float exposure, float distanceFraction, float deltaTime)
        {
            if (Stage == DetectionStage.Spotted) return;

            UpdateAwareness(seen, exposure, distanceFraction, deltaTime);

            if (Stage == DetectionStage.Unaware)
            {
                if (Awareness >= SuspicionThreshold)
                {
                    Stage = DetectionStage.Suspicious;
                    secondsSuspicious = 0f;
                }
                return;
            }

            secondsSuspicious += deltaTime;
            if (Awareness <= 0f) Stage = DetectionStage.Unaware;
            else if (Awareness >= 1f && secondsSuspicious >= WarningSeconds) Stage = DetectionStage.Spotted;
        }

        public void Reset()
        {
            Awareness = 0f;
            secondsSuspicious = 0f;
            Stage = DetectionStage.Unaware;
        }

        void UpdateAwareness(bool seen, float exposure, float distanceFraction, float deltaTime)
        {
            if (seen && exposure > 0f)
            {
                float closeness = Mathf.Lerp(GainAtMaxRange, 1f, 1f - Mathf.Clamp01(distanceFraction));
                Awareness = Mathf.Clamp01(Awareness + GainPerSecond * Mathf.Clamp01(exposure) * closeness * deltaTime);
            }
            else
            {
                Awareness = Mathf.Clamp01(Awareness - LossPerSecond * deltaTime);
            }
        }
    }
}
