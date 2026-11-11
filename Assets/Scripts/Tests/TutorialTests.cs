using System.Collections.Generic;
using NUnit.Framework;

namespace LastLamp.Tests
{
    public class TutorialTests
    {
        const float Min = 2f;
        const float Away = 3f;

        static TutorialQueue NewQueue(HashSet<string> seen = null)
        {
            return new TutorialQueue(seen ?? new HashSet<string>(), Min, Away);
        }

        [Test]
        public void FillPutsInTheKeyForEachAction()
        {
            string text = TutorialRules.Fill("{Dodge}  Dodge, {Attack}  Punch", action => action == "Dodge" ? "Right mouse" : "Left mouse");
            Assert.That(text, Is.EqualTo("Right mouse  Dodge, Left mouse  Punch"));
        }

        [Test]
        public void FillLeavesAnUnknownActionAsItIs()
        {
            Assert.That(TutorialRules.Fill("Hold {Nothing}", action => null), Is.EqualTo("Hold {Nothing}"));
        }

        [Test]
        public void MouseButtonsAreSpelledOut()
        {
            Assert.That(TutorialRules.FriendlyKey("LMB"), Is.EqualTo("Left mouse"));
            Assert.That(TutorialRules.FriendlyKey("RMB"), Is.EqualTo("Right mouse"));
            Assert.That(TutorialRules.FriendlyKey("Left Shift"), Is.EqualTo("Left Shift"));
        }

        [Test]
        public void MovementKeysReadAsWasd()
        {
            var keys = new Dictionary<string, string> { { "up", "W" }, { "down", "S" }, { "left", "A" }, { "right", "D" } };
            Assert.That(TutorialRules.JoinDirections(keys), Is.EqualTo("WASD"));
        }

        [Test]
        public void LongerMovementKeyNamesAreSeparated()
        {
            var keys = new Dictionary<string, string> { { "up", "Up Arrow" }, { "down", "Down Arrow" }, { "left", "Left Arrow" }, { "right", "Right Arrow" } };
            Assert.That(TutorialRules.JoinDirections(keys), Is.EqualTo("Up Arrow/Left Arrow/Down Arrow/Right Arrow"));
        }

        [Test]
        public void PromptsShowOneAtATimeInTheOrderAsked()
        {
            var queue = NewQueue();
            queue.Want("move", false);
            queue.Want("sprint", false);
            queue.Tick(0.1f, false, true);
            Assert.That(queue.Current, Is.EqualTo("move"));

            queue.Tick(Min, true, true);
            Assert.That(queue.Current, Is.EqualTo("sprint"));
        }

        [Test]
        public void ADonePromptStaysForTheMinimumTime()
        {
            var queue = NewQueue();
            queue.Want("jump", false);
            queue.Tick(0.1f, false, true);
            queue.Tick(Min * 0.5f, true, true);
            Assert.That(queue.Current, Is.EqualTo("jump"));

            queue.Tick(Min, true, true);
            Assert.That(queue.Current, Is.Null);
        }

        [Test]
        public void AnUndonePromptStaysWhileThePlayerIsStillThere()
        {
            var queue = NewQueue();
            queue.Want("sprint", false);
            queue.Want("jump", false);
            queue.Tick(0.1f, false, true);
            for (int i = 0; i < 20; i++) queue.Tick(1f, false, true);
            Assert.That(queue.Current, Is.EqualTo("sprint"));
        }

        [Test]
        public void LeavingASpotLetsTheNextPromptTakeOver()
        {
            var queue = NewQueue();
            queue.Want("sprint", false);
            queue.Tick(0.1f, false, true);
            queue.Tick(Min, false, true);
            queue.Want("light", false);
            queue.Tick(0.1f, false, false);
            Assert.That(queue.Current, Is.EqualTo("light"));
        }

        [Test]
        public void APromptGoesAfterThePlayerHasBeenAwayAWhileEvenWithNothingWaiting()
        {
            var queue = NewQueue();
            queue.Want("sprint", false);
            queue.Tick(0.1f, false, true);
            queue.Tick(Min, false, false);
            Assert.That(queue.Current, Is.EqualTo("sprint"));

            queue.Tick(Away, false, false);
            Assert.That(queue.Current, Is.Null);
        }

        [Test]
        public void AnUrgentPromptJumpsTheQueueAndTakesOverAfterTheMinimum()
        {
            var queue = NewQueue();
            queue.Want("sprint", false);
            queue.Want("jump", false);
            queue.Tick(0.1f, false, true);
            queue.Want("noticed", true);
            queue.Tick(0.1f, false, true);
            Assert.That(queue.Current, Is.EqualTo("sprint"));

            queue.Tick(Min, false, true);
            Assert.That(queue.Current, Is.EqualTo("noticed"));
        }

        [Test]
        public void ADroppedPromptNeverShows()
        {
            var queue = NewQueue();
            queue.Want("move", false);
            queue.Want("sprint", false);
            queue.Tick(0.1f, false, true);
            queue.Drop("sprint");
            queue.Tick(Min, true, true);
            Assert.That(queue.Current, Is.Null);
        }

        [Test]
        public void APromptShowsOnlyOnceEvenAfterARestart()
        {
            var seen = new HashSet<string>();
            var queue = NewQueue(seen);
            queue.Want("move", false);
            queue.Tick(0.1f, false, true);
            queue.Tick(Min, true, true);
            queue.Want("move", false);
            queue.Tick(0.1f, false, true);
            Assert.That(queue.Current, Is.Null);

            var afterRestart = NewQueue(seen);
            afterRestart.Want("move", false);
            afterRestart.Tick(0.1f, false, true);
            Assert.That(afterRestart.Current, Is.Null);
        }
    }
}
