using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class NoiseTests
    {
        [Test]
        public void SprintingIsLouderThanWalking()
        {
            Assert.That(NoiseRules.Footstep(true, false), Is.GreaterThan(NoiseRules.Footstep(false, false)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CrouchedFootstepsAreSilent(bool sprinting)
        {
            Assert.That(NoiseRules.Footstep(sprinting, true), Is.EqualTo(0f));
        }

        [Test]
        public void CrouchedLandingIsQuieterButNotSilent()
        {
            float crouched = NoiseRules.Landing(true);

            Assert.That(crouched, Is.GreaterThan(0f));
            Assert.That(crouched, Is.LessThan(NoiseRules.Landing(false)));
        }

        [Test]
        public void FootstepsInALampPoolAreSilentToEnemies()
        {
            Assert.That(NoiseRules.Footstep(false, false, true), Is.EqualTo(0f));
            Assert.That(NoiseRules.Footstep(true, false, true), Is.EqualTo(0f));
        }

        [Test]
        public void LandingInALampPoolIsSilentToEnemies()
        {
            Assert.That(NoiseRules.Landing(false, true), Is.EqualTo(0f));
            Assert.That(NoiseRules.Landing(true, true), Is.EqualTo(0f));
        }

        [Test]
        public void EmitPublishesPositionAndRadius()
        {
            var heard = Listen(() => Noise.Emit(new Vector3(1f, 2f, 3f), 6f));

            Assert.That(heard.Count, Is.EqualTo(1));
            Assert.That(heard[0].Position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(heard[0].Radius, Is.EqualTo(6f));
        }

        [Test]
        public void ZeroRadiusIsNotEmitted()
        {
            var heard = Listen(() => Noise.Emit(Vector3.zero, 0f));

            Assert.That(heard, Is.Empty);
        }

        static List<NoiseEvent> Listen(System.Action action)
        {
            var heard = new List<NoiseEvent>();
            void Record(NoiseEvent noise) => heard.Add(noise);

            Noise.Emitted += Record;
            try
            {
                action();
            }
            finally
            {
                Noise.Emitted -= Record;
            }
            return heard;
        }
    }
}
