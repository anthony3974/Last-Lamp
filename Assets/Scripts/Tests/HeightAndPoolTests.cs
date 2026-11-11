using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class HeightAndPoolTests
    {
        [TestCase(0f, 0f, true)]
        [TestCase(0f, 2.5f, true)]
        [TestCase(0f, 8f, false)]
        [TestCase(8f, 0f, false)]
        public void SameLevelSeparatesFloors(float a, float b, bool expected)
        {
            Assert.That(LevelRules.SameLevel(new Vector3(1f, a, 1f), new Vector3(1f, b, 1f)), Is.EqualTo(expected));
        }

        [Test]
        public void APunchDoesNotReachAnotherFloor()
        {
            bool lands = CombatRules.InPunchArc(Vector3.zero, Vector3.forward, new Vector3(0f, 8f, 1f), 2f, 75f);

            Assert.That(lands, Is.False);
        }

        [TestCase(3.7f, 0f, true)]
        [TestCase(11f, 8f, true)]
        [TestCase(11f, 0f, false)]
        [TestCase(4f, 8f, false)]
        public void APoolOnlyCoversTheFloorBelowIt(float zoneY, float pointY, bool expected)
        {
            Assert.That(LampZone.IsWithinHeight(zoneY, 4.35f, pointY), Is.EqualTo(expected));
        }

        [TestCase(2.9f, -2.9f, true)]
        [TestCase(3.1f, 0f, false)]
        [TestCase(0f, -3.1f, false)]
        public void SquarePoolEndsAtItsSides(float x, float z, bool expected)
        {
            Assert.That(LampZone.IsInsideSquare(x, z, 3f), Is.EqualTo(expected));
        }

        [TestCase(2.5f, 0f, false)]
        [TestCase(1.9f, 0f, true)]
        [TestCase(3.4f, 6f, true)]
        [TestCase(3.6f, 6f, false)]
        [TestCase(0f, 6.1f, false)]
        [TestCase(0f, -0.1f, false)]
        public void WedgePoolWidensAwayFromItsStart(float x, float z, bool expected)
        {
            Assert.That(LampZone.IsInsideWedge(x, z, 6f, 4f, 7f), Is.EqualTo(expected));
        }
    }
}
