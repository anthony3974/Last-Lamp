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

        static EnemySenses SeenInReach()
        {
            var senses = SeenInTheDark();
            senses.PlayerInReach = true;
            return senses;
        }

        [Test]
        public void AnEnemyInReachWindsUpBeforeCatching()
        {
            var brain = BrainInChase();
            var close = SeenInReach();

            brain.Tick(close, Frame);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Attack));
            Assert.That(brain.CaughtPlayer, Is.False);

            brain.Tick(close, EnemyBrain.AttackWindupSeconds);
            Assert.That(brain.CaughtPlayer, Is.True);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Patrol));
        }

        [Test]
        public void CatchingOnlyLastsOneTick()
        {
            var brain = BrainInChase();
            var close = SeenInReach();
            brain.Tick(close, Frame);
            brain.Tick(close, EnemyBrain.AttackWindupSeconds);

            brain.Tick(new EnemySenses(), Frame);

            Assert.That(brain.CaughtPlayer, Is.False);
        }

        [Test]
        public void DodgingDuringTheWindupAvoidsBeingCaught()
        {
            var brain = BrainInChase();
            var close = SeenInReach();
            brain.Tick(close, Frame);
            close.PlayerDodging = true;

            brain.Tick(close, EnemyBrain.AttackWindupSeconds);

            Assert.That(brain.CaughtPlayer, Is.False);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
        }

        [Test]
        public void GettingOutOfReachDuringTheWindupAvoidsBeingCaught()
        {
            var brain = BrainInChase();
            brain.Tick(SeenInReach(), Frame);

            brain.Tick(SeenInTheDark(), EnemyBrain.AttackWindupSeconds);

            Assert.That(brain.CaughtPlayer, Is.False);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
        }

        [Test]
        public void LampLightDoesNotStopACatch()
        {
            var brain = BrainInChase();
            var inLamp = new EnemySenses { SeesPlayer = true, Exposure = 0f, PlayerInReach = true };

            brain.Tick(inLamp, Frame);
            brain.Tick(inLamp, EnemyBrain.AttackWindupSeconds);

            Assert.That(brain.CaughtPlayer, Is.True);
        }

        [Test]
        public void AnEnemyThatWatchedThePlayerHideDragsThemOut()
        {
            var brain = BrainInChase();
            var hidden = new EnemySenses { PlayerHidden = true, PlayerInReach = true };

            brain.Tick(hidden, Frame);
            brain.Tick(hidden, EnemyBrain.AttackWindupSeconds);

            Assert.That(brain.CaughtPlayer, Is.True);
        }

        [Test]
        public void AnEnemyThatDidNotSeeThePlayerHideCannotCatchThem()
        {
            var brain = BrainInChase();
            brain.Tick(new EnemySenses(), Frame);
            var hidden = new EnemySenses { PlayerHidden = true, PlayerInReach = true };

            for (int i = 0; i < 1200; i++)
            {
                brain.Tick(hidden, Frame);
                Assert.That(brain.CaughtPlayer, Is.False);
                Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Attack));
            }
        }

        [Test]
        public void APunchStunsAndCancelsACatch()
        {
            var brain = BrainInChase();
            var close = SeenInReach();
            brain.Tick(close, Frame);

            Assert.That(brain.TryStun(Vector3.zero), Is.True);
            brain.Tick(close, EnemyBrain.AttackWindupSeconds);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Stunned));
            Assert.That(brain.CaughtPlayer, Is.False);
        }

        [Test]
        public void AChaseResumesAfterAStun()
        {
            var brain = BrainInChase();
            brain.TryStun(Vector3.zero);

            brain.Tick(SeenInTheDark(), EnemyBrain.StunSeconds + Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
        }

        [Test]
        public void PunchingAnUnawareEnemySendsItSearchingWithoutSpottingThePlayer()
        {
            var brain = new EnemyBrain();
            brain.TryStun(Vector3.one);

            brain.Tick(new EnemySenses(), EnemyBrain.StunSeconds + Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));
            Assert.That(brain.LastKnownPosition, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void AnEnemyCannotBeStunLocked()
        {
            var brain = BrainInChase();
            Assert.That(brain.TryStun(Vector3.zero), Is.True);
            Assert.That(brain.TryStun(Vector3.zero), Is.False);

            brain.Tick(SeenInTheDark(), EnemyBrain.StunSeconds + Frame);
            Assert.That(brain.TryStun(Vector3.zero), Is.False);

            brain.Tick(SeenInTheDark(), EnemyBrain.StunImmunitySeconds);
            Assert.That(brain.TryStun(Vector3.zero), Is.True);
        }

        [TestCase(0f, 1f, true)]
        [TestCase(0f, 3f, false)]
        [TestCase(0f, -1f, false)]
        public void PunchesOnlyLandInFrontAndInReach(float x, float z, bool lands)
        {
            bool result = CombatRules.InPunchArc(Vector3.zero, Vector3.forward, new Vector3(x, 0f, z), 2f, 75f);

            Assert.That(result, Is.EqualTo(lands));
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
        public void AChaseKeepsGoingWhileThePlayerIsInLampLight()
        {
            var brain = BrainInChase();
            var inLamp = new EnemySenses { SeesPlayer = true, Exposure = 0f, PlayerPosition = Vector3.one };

            for (int i = 0; i < 600; i++) brain.Tick(inLamp, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.LastKnownPosition, Is.EqualTo(Vector3.one));
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

        [Test]
        public void AResetEnemyForgetsTheChaseAndMustNoticeThePlayerAfresh()
        {
            var brain = BrainInChase();

            brain.Reset();
            Assert.That(brain.State, Is.EqualTo(EnemyState.Patrol));

            brain.Tick(SeenInTheDark(), 10f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Suspicious));
        }

        [Test]
        public void AResetEnemyCanBeStunnedStraightAway()
        {
            var brain = BrainInChase();
            Assert.That(brain.TryStun(Vector3.zero), Is.True);

            brain.Reset();

            Assert.That(brain.TryStun(Vector3.zero), Is.True);
        }

        [Test]
        public void AfterHidingUnseenTheEnemyKeepsGoingUntilItReachesWhereItLostThePlayer()
        {
            var brain = BrainInChase();
            brain.Tick(new EnemySenses(), Frame);

            brain.Tick(new EnemySenses { PlayerHidden = true }, Frame);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));

            brain.Tick(new EnemySenses { PlayerHidden = true, Arrived = true }, Frame);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));
        }

        [Test]
        public void SearchLastsLongerAfterThePlayerHides()
        {
            var brain = BrainInChase();
            brain.Tick(new EnemySenses(), Frame);
            var hidden = new EnemySenses { PlayerHidden = true };
            brain.Tick(hidden, Frame);
            brain.Tick(new EnemySenses { PlayerHidden = true, Arrived = true }, Frame);

            brain.Tick(hidden, EnemyBrain.SearchSeconds + 1f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));

            brain.Tick(hidden, EnemyBrain.HiddenSearchSeconds);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Patrol));
        }

        [Test]
        public void HidingInFullViewDoesNotEndAChase()
        {
            var brain = BrainInChase();
            var hidden = new EnemySenses { PlayerHidden = true, PlayerPosition = Vector3.one };

            for (int i = 0; i < 600; i++) brain.Tick(hidden, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
            Assert.That(brain.LastKnownPosition, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void ASuspiciousEnemyThatWatchesThePlayerHideStillGivesAWarningFirst()
        {
            var brain = new EnemyBrain();
            var seen = SeenInTheDark();
            for (int i = 0; i < 600 && brain.State != EnemyState.Suspicious; i++) brain.Tick(seen, Frame);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Suspicious));

            var hidden = new EnemySenses { PlayerHidden = true };
            for (int i = 0; i < 600 && brain.State != EnemyState.Chase; i++) brain.Tick(hidden, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase));
        }

        [Test]
        public void AnEnemyThatHasNotNoticedThePlayerDoesNotSeeThemHide()
        {
            var brain = new EnemyBrain();
            brain.Tick(SeenInTheDark(), Frame);
            var hidden = new EnemySenses { PlayerHidden = true, SeesPlayer = true, Exposure = 1f };

            for (int i = 0; i < 600; i++) brain.Tick(hidden, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Patrol));
        }

        [Test]
        public void WatchingThePlayerHideIsForgottenOnceTheyComeOut()
        {
            var brain = BrainInChase();
            brain.Tick(new EnemySenses { PlayerHidden = true }, Frame);
            brain.Tick(new EnemySenses(), Frame);

            brain.Tick(new EnemySenses { PlayerHidden = true }, Frame);
            brain.Tick(new EnemySenses { PlayerHidden = true, Arrived = true }, Frame);

            Assert.That(brain.State, Is.EqualTo(EnemyState.Search));
        }
    }
}
