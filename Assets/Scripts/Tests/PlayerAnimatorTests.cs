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
    }
}
