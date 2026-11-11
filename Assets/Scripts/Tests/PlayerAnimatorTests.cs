using NUnit.Framework;

namespace LastLamp.Tests
{
    public class PlayerAnimatorTests
    {
        [Test]
        public void StandingNeverDropsTheHips()
        {
            Assert.That(PlayerAnimator.HipDrop(false, 3f, 1.5f, 0.25f), Is.EqualTo(0f));
        }

        [Test]
        public void CrouchedAndStillDropsNothingBecauseTheIdleIsAlreadyLow()
        {
            Assert.That(PlayerAnimator.HipDrop(true, 0f, 1.5f, 0.25f), Is.EqualTo(0f));
        }

        [Test]
        public void CrouchWalkingDropsWithSpeedAndStopsAtTheLimit()
        {
            Assert.That(PlayerAnimator.HipDrop(true, 0.75f, 1.5f, 0.25f), Is.EqualTo(0.125f).Within(0.001f));
            Assert.That(PlayerAnimator.HipDrop(true, 5f, 1.5f, 0.25f), Is.EqualTo(0.25f));
        }

        [Test]
        public void LookAheadOnlyWhenCrouchWalking()
        {
            Assert.That(PlayerAnimator.CrouchWalkAmount(false, 3f, 1.5f), Is.EqualTo(0f));
            Assert.That(PlayerAnimator.CrouchWalkAmount(true, 0f, 1.5f), Is.EqualTo(0f));
            Assert.That(PlayerAnimator.CrouchWalkAmount(true, 1.5f, 1.5f), Is.EqualTo(1f));
        }

        [Test]
        public void StandingUpFadesTheLookOutInsteadOfSnapping()
        {
            // One 60 fps frame after standing up from a full crouch walk, with a 0.25 s fade.
            float next = PlayerAnimator.FadeTowards(1f, 0f, 0.25f, 1f / 60f);
            Assert.That(next, Is.LessThan(1f));
            Assert.That(next, Is.GreaterThan(0.9f));
        }

        [Test]
        public void TheFadeFinishesInItsFadeTimeAndNeverOvershoots()
        {
            float value = 1f;
            for (int frame = 0; frame < 15; frame++) value = PlayerAnimator.FadeTowards(value, 0f, 0.25f, 1f / 60f);
            Assert.That(value, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PlayerAnimator.FadeTowards(0.2f, 0f, 0.25f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void ZeroFadeTimeJumpsStraightToTheTarget()
        {
            Assert.That(PlayerAnimator.FadeTowards(0f, 1f, 0f, 0.016f), Is.EqualTo(1f));
        }
    }
}
