using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LastLamp
{
    /// <summary>
    /// The tutorial's text rules, in plain C# so they can be tested: filling in key names and making them readable.
    /// </summary>
    internal static class TutorialRules
    {
        static readonly Regex Token = new Regex(@"\{(\w+)\}");
        static readonly string[] DirectionOrder = { "up", "left", "down", "right" };

        /// <summary>Replaces each {ActionName} with the key for that action. A token with no key is left as it is.</summary>
        public static string Fill(string template, Func<string, string> keyFor)
        {
            return Token.Replace(template, match => keyFor(match.Groups[1].Value) ?? match.Value);
        }

        /// <summary>Turns the Input System's short mouse names into words.</summary>
        public static string FriendlyKey(string display)
        {
            switch (display)
            {
                case "LMB": return "Left mouse";
                case "RMB": return "Right mouse";
                case "MMB": return "Middle mouse";
                default: return display;
            }
        }

        /// <summary>Joins a movement composite's keys in reading order, so W, S, A, D reads as WASD.</summary>
        public static string JoinDirections(IDictionary<string, string> keyByDirection)
        {
            var keys = new List<string>();
            foreach (string direction in DirectionOrder)
            {
                if (keyByDirection.TryGetValue(direction, out string key)) keys.Add(key);
            }
            bool allSingleLetters = keys.TrueForAll(key => key.Length == 1);
            return string.Join(allSingleLetters ? "" : "/", keys);
        }
    }
}
