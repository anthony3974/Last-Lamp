using System.Collections.Generic;

namespace LastLamp
{
    /// <summary>
    /// Decides which tutorial prompt is on screen, in plain C# so it can be tested.
    /// One prompt shows at a time, and each shows only once. A prompt stays up for at least a minimum time, then
    /// goes when its task is done, when the player has walked away from it and another wants the screen, when an
    /// urgent one (a monster noticing, getting caught) is waiting, or when the player has been away from it a while.
    /// </summary>
    internal class TutorialQueue
    {
        struct Waiting
        {
            public string Id;
            public bool Urgent;
        }

        readonly ISet<string> seen;
        readonly float minSeconds;
        readonly float awaySeconds;
        readonly List<Waiting> waiting = new List<Waiting>();
        float awayFor;

        public string Current { get; private set; }
        public float ShownFor { get; private set; }

        /// <param name="seen">Prompts already shown. Pass the same set to a new queue to keep them from showing again.</param>
        public TutorialQueue(ISet<string> seen, float minSeconds, float awaySeconds)
        {
            this.seen = seen;
            this.minSeconds = minSeconds;
            this.awaySeconds = awaySeconds;
        }

        /// <summary>Asks to show a prompt. Asking again while it waits, or after it has shown, does nothing.</summary>
        public void Want(string id, bool urgent)
        {
            if (id == Current || seen.Contains(id) || waiting.Exists(entry => entry.Id == id)) return;
            var entry = new Waiting { Id = id, Urgent = urgent };
            if (urgent) waiting.Insert(0, entry);
            else waiting.Add(entry);
        }

        /// <summary>Stops waiting for a prompt, because the player left its spot before it could show.</summary>
        public void Drop(string id)
        {
            waiting.RemoveAll(entry => entry.Id == id);
        }

        /// <param name="currentDone">The current prompt's task is done.</param>
        /// <param name="currentHere">The player is still at the current prompt's spot.</param>
        /// <returns>True when a different prompt, or none, is now current.</returns>
        public bool Tick(float deltaTime, bool currentDone, bool currentHere)
        {
            string before = Current;
            if (Current != null)
            {
                ShownFor += deltaTime;
                awayFor = currentHere ? 0f : awayFor + deltaTime;
                if (ShownFor < minSeconds) return false;

                bool replaced = waiting.Count > 0 && (!currentHere || waiting[0].Urgent);
                if (!currentDone && !replaced && awayFor < awaySeconds) return false;
                Current = null;
            }

            if (waiting.Count > 0)
            {
                Current = waiting[0].Id;
                waiting.RemoveAt(0);
                seen.Add(Current);
                ShownFor = 0f;
                awayFor = 0f;
            }
            return Current != before;
        }
    }
}
