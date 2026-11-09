using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class EnemyTests
    {
        const float Frame = 1f / 60f;

        static EnemySenses SeenInTheDark(float exposure = 1f) => new EnemySenses
        {
            SeesPlayer = true,
            Exposure = exposure,
            DistanceFraction = 0f,
            PlayerPosition = new Vector3(1f, 0f, 2f),
        };

        static EnemyBrain BrainInChase()
        {
            var brain = new EnemyBrain();
            var seen = SeenInTheDark();
            for (int i = 0; i < 600 && brain.State != EnemyState.Chase; i++) brain.Tick(seen, Frame);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
            return brain;
        }

        [TestCase(1f / 60f)]
        [TestCase(0.1f)]
        [TestCase(10f)]
        public void DetectionAlwaysPassesThroughSuspicious(float deltaTime)
        {
            var meter = new DetectionMeter();
            bool wasSuspicious = false;

            for (int i = 0; i < 2000 && meter.Stage != DetectionStage.Spotted; i++)
            {
                meter.Tick(true, 1f, 0f, deltaTime);
                wasSuspicious |= meter.Stage == DetectionStage.Suspicious;
            }

            Assert.That(meter.Stage, Is.EqualTo(DetectionStage.Spotted));
            Assert.That(wasSuspicious, Is.True);
        }

        [Test]
        public void WarningLastsAtLeastWarningSeconds()
        {
            var meter = new DetectionMeter();
            float secondsSuspicious = 0f;

            while (meter.Stage != DetectionStage.Spotted)
            {
                meter.Tick(true, 1f, 0f, Frame);
                if (meter.Stage == DetectionStage.Suspicious) secondsSuspicious += Frame;
            }

            Assert.That(secondsSuspicious, Is.GreaterThanOrEqualTo(DetectionMeter.WarningSeconds - Frame * 2f));
        }

        [Test]
        public void ZeroExposureNeverRaisesAwareness()
        {
            var meter = new DetectionMeter();
            for (int i = 0; i < 600; i++) meter.Tick(true, 0f, 0f, Frame);

            Assert.That(meter.Awareness, Is.EqualTo(0f));
            Assert.That(meter.Stage, Is.EqualTo(DetectionStage.Unaware));
        }

        [Test]
        public void CloseEnemiesNoticeFasterThanFarOnes()
        {
            var close = new DetectionMeter();
            var far = new DetectionMeter();
            close.Tick(true, 0.5f, 0f, 0.1f);
            far.Tick(true, 0.5f, 1f, 0.1f);

            Assert.That(close.Awareness, Is.GreaterThan(far.Awareness));
        }

        [Test]
        public void HearingANoiseGivesAWarningFirst()
        {
            var brain = new EnemyBrain();
            brain.Tick(new EnemySenses { HeardNoise = true, NoisePosition = Vector3.one }, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Suspicious));
            Assert.That(brain.LastKnownPosition, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void SuspiciousBecomesInvestigateIfNotSpotted()
        {
            var brain = new EnemyBrain();
            brain.Tick(new EnemySenses { HeardNoise = true }, Frame);

            brain.Tick(new EnemySenses(), EnemyBrain.SuspiciousSeconds + Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Investigate));
        }

        [Test]
        public void InvestigateBecomesSearchOnArrival()
        {
            var brain = new EnemyBrain();
            brain.Tick(new EnemySenses { HeardNoise = true }, Frame);
            brain.Tick(new EnemySenses(), EnemyBrain.SuspiciousSeconds + Frame);

            brain.Tick(new EnemySenses { Arrived = true }, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));
        }

        [Test]
        public void EnemyGivesUpAfterWaitingAtALamp()
        {
            var brain = BrainInChase();
            var inLamp = new EnemySenses { SeesPlayer = true, Exposure = 0f, PlayerInLamp = true };

            brain.Tick(inLamp, EnemyBrain.LampEdgeWaitSeconds - 0.5f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));

            brain.Tick(inLamp, 1f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));
        }

        [Test]
        public void ChaseEndsWhenThePlayerIsOutOfSight()
        {
            var brain = BrainInChase();

            brain.Tick(new EnemySenses(), EnemyBrain.LoseSightSeconds + Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));
        }

        [Test]
        public void SearchReturnsToPatrol()
        {
            var brain = BrainInChase();
            brain.Tick(new EnemySenses(), EnemyBrain.LoseSightSeconds + Frame);

            brain.Tick(new EnemySenses(), EnemyBrain.SearchSeconds + Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Patrol));
        }

        [Test]
        public void SpottedAgainAfterLosingThePlayerStillGetsAWarning()
        {
            var brain = BrainInChase();
            brain.Tick(new EnemySenses(), EnemyBrain.LoseSightSeconds + Frame);
            var seen = SeenInTheDark();

            brain.Tick(seen, 10f);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Suspicious));
        }
    }
}
