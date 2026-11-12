using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class DoorTests
    {
        const float HingeX = 1.8f;
        const float Clearance = 0.45f;

        [Test]
        public void ComingThroughToTheInsideCounts()
        {
            Assert.That(DoorRules.CameInside(new Vector3(0f, 0f, -0.1f), new Vector3(0f, 0f, 0.1f), 1, 2.4f), Is.True);
            Assert.That(DoorRules.CameInside(new Vector3(0f, 0f, 0.1f), new Vector3(0f, 0f, -0.1f), -1, 2.4f), Is.True);
        }

        [Test]
        public void GoingOutOrStayingOnOneSideDoesNot()
        {
            Assert.That(DoorRules.CameInside(new Vector3(0f, 0f, 0.1f), new Vector3(0f, 0f, -0.1f), 1, 2.4f), Is.False);
            Assert.That(DoorRules.CameInside(new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, 0.6f), 1, 2.4f), Is.False);
            Assert.That(DoorRules.CameInside(new Vector3(0f, 0f, -0.6f), new Vector3(0f, 0f, -0.5f), 1, 2.4f), Is.False);
        }

        [Test]
        public void CrossingTheDoorLineFarFromTheDoorwayDoesNot()
        {
            Assert.That(DoorRules.CameInside(new Vector3(10f, 0f, -0.1f), new Vector3(10f, 0f, 0.1f), 1, 2.4f), Is.False);
        }

        [Test]
        public void SwingsAwayFromThePlayer()
        {
            Assert.That(DoorRules.SwingSide(new Vector3(0f, 0f, -2f)), Is.EqualTo(1));
            Assert.That(DoorRules.SwingSide(new Vector3(0f, 0f, 2f)), Is.EqualTo(-1));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void BothLeavesOpenToTheSameSide(int side)
        {
            var (left, right) = DoorRules.LeafAngles(side, 1f, 90f);

            // The left leaf lies along +X when shut, the right along -X. Turned, each should point to the open side.
            Vector3 leftTip = Quaternion.Euler(0f, left, 0f) * Vector3.right;
            Vector3 rightTip = Quaternion.Euler(0f, right, 0f) * Vector3.left;

            Assert.That(leftTip.z, Is.EqualTo(side).Within(1e-4f));
            Assert.That(rightTip.z, Is.EqualTo(side).Within(1e-4f));
        }

        [Test]
        public void ShutAtZeroAndFullyOpenAtOne()
        {
            Assert.That(DoorRules.LeafAngles(1, 0f, 90f), Is.EqualTo((0f, 0f)));
            Assert.That(DoorRules.LeafAngles(1, 1f, 90f), Is.EqualTo((-90f, 90f)));
        }

        [Test]
        public void SwingNeverOvershoots()
        {
            for (float amount = 0f; amount <= 1f; amount += 0.05f)
            {
                var (_, right) = DoorRules.LeafAngles(1, amount, 90f);
                Assert.That(right, Is.InRange(0f, 90f));
            }
        }

        [Test]
        public void OpeningNeverSweepsThroughThePlayer()
        {
            // Wherever the player stands to open it, the leaves swing to the far side, clear of them.
            foreach (var player in new[] { new Vector3(0f, 0f, -0.7f), new Vector3(-1.5f, 0f, -0.8f), new Vector3(1.9f, 0f, 1f) })
            {
                int side = DoorRules.SwingSide(player);
                Assert.That(player.z * side, Is.LessThan(0f), $"player at {player}");
            }
        }

        [Test]
        public void CannotCloseOnAPlayerInTheDoorway()
        {
            Assert.That(DoorRules.BlocksClosing(new Vector3(0.5f, 0f, 0f), HingeX, 1, Clearance), Is.True);
        }

        [Test]
        public void CannotCloseOnAPlayerBesideAnOpenLeaf()
        {
            // The leaves are open toward +Z; the player stands just inside, next to the left one.
            Assert.That(DoorRules.BlocksClosing(new Vector3(-1.2f, 0f, 1f), HingeX, 1, Clearance), Is.True);
        }

        [Test]
        public void CanCloseFromTheOtherSide()
        {
            Assert.That(DoorRules.BlocksClosing(new Vector3(0f, 0f, -1.2f), HingeX, 1, Clearance), Is.False);
        }

        [Test]
        public void CanCloseFromBeyondTheLeaves()
        {
            Assert.That(DoorRules.BlocksClosing(new Vector3(0f, 0f, 2.3f), HingeX, 1, Clearance), Is.False);
        }
    }
}
