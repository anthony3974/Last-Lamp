using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class CoatSkirtTests
    {
        static readonly Vector3 Down = Vector3.down;
        static readonly Vector3 Forward = Vector3.forward;

        [Test]
        public void StraightDownIsZero()
        {
            Assert.That(CoatSkirt.AngleFromDown(Vector3.up, Vector3.zero, Down, Forward), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void PointOutwardIsPositiveAndInwardIsNegative()
        {
            Assert.That(CoatSkirt.AngleFromDown(Vector3.up, new Vector3(0f, 0f, 1f), Down, Forward), Is.EqualTo(45f).Within(0.01f));
            Assert.That(CoatSkirt.AngleFromDown(Vector3.up, new Vector3(0f, 0f, -1f), Down, Forward), Is.EqualTo(-45f).Within(0.01f));
        }

        [Test]
        public void UpperStaysAtRestWhileTheLegIsBehindTheCloth()
        {
            Assert.That(CoatSkirt.UpperSwing(-30f, 5f, 65f), Is.EqualTo(0f));
        }

        [Test]
        public void UpperSwingsOutToClearTheLegAndStopsAtTheLimit()
        {
            Assert.That(CoatSkirt.UpperSwing(20f, 5f, 65f), Is.GreaterThan(15f));
            Assert.That(CoatSkirt.UpperSwing(120f, 5f, 65f), Is.EqualTo(65f));
        }

        [Test]
        public void LowerHangsBackWhenNothingPushesIt()
        {
            Assert.That(CoatSkirt.LowerSwing(-40f, 3f, 50f, 45f), Is.EqualTo(-30f).Within(0.01f));
        }

        [Test]
        public void LowerNeverSwingsPastItsLimit()
        {
            Assert.That(CoatSkirt.LowerSwing(170f, 3f, 0f, 45f), Is.EqualTo(45f));
        }
    }
}
