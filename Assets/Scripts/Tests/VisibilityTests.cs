using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class VisibilityTests
    {
        const float Sprint = 5.5f;
        const float Walk = 3f;

        [TestCase(0f, false)]
        [TestCase(Walk, false)]
        [TestCase(Sprint, false)]
        [TestCase(Sprint, true)]
        public void LampPoolIsAlwaysSafe(float speed, bool crouching)
        {
            Assert.That(VisibilityRules.Target(true, speed, Sprint, crouching), Is.EqualTo(0f));
        }

        [Test]
        public void FasterMeansMoreExposed()
        {
            float still = VisibilityRules.Target(false, 0f, Sprint, false);
            float walking = VisibilityRules.Target(false, Walk, Sprint, false);
            float sprinting = VisibilityRules.Target(false, Sprint, Sprint, false);

            Assert.That(still, Is.GreaterThan(0f));
            Assert.That(walking, Is.GreaterThan(still));
            Assert.That(sprinting, Is.EqualTo(1f));
        }

        [Test]
        public void CrouchingLowersExposure()
        {
            float standing = VisibilityRules.Target(false, Walk, Sprint, false);
            float crouching = VisibilityRules.Target(false, Walk, Sprint, true);

            Assert.That(crouching, Is.LessThan(standing));
        }

        [Test]
        public void LandingInTheDarkSpikesExposure()
        {
            float standing = VisibilityRules.Target(false, 0f, Sprint, false);

            Assert.That(VisibilityRules.Landing(false, false), Is.GreaterThan(standing));
        }

        [Test]
        public void LandingCrouchedIsQuieter()
        {
            Assert.That(VisibilityRules.Landing(false, true), Is.LessThan(VisibilityRules.Landing(false, false)));
        }

        [Test]
        public void LandingInALampIsStillSafe()
        {
            Assert.That(VisibilityRules.Landing(true, false), Is.EqualTo(0f));
        }

        [TestCase(0f, 0f, true)]
        [TestCase(2.5f, 0f, true)]
        [TestCase(3f, 0f, false)]
        [TestCase(2f, 2f, false)]
        public void ZoneUsesGroundDistance(float x, float z, bool expected)
        {
            var lamp = new Vector3(0f, 3.7f, 0f);
            var point = new Vector3(x, 0f, z);

            Assert.That(LampZone.IsInside(lamp, 2.6f, point), Is.EqualTo(expected));
        }
    }
}
