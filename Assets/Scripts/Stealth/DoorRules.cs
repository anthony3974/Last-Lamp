using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// The geometry of a double door, kept apart from the component so it can be tested.
    /// Positions are in the door's own space: X along the doorway, Z through it, the door's centre at the origin.
    /// </summary>
    internal static class DoorRules
    {
        /// <summary>How deep the doorway is, from its centre line to either face, in metres.</summary>
        public const float DoorwayHalfDepth = 0.25f;

        /// <returns>+1 to swing the leaves toward +Z, -1 toward -Z: always away from the player.</returns>
        public static int SwingSide(Vector3 localPlayer)
        {
            return localPlayer.z < 0f ? 1 : -1;
        }

        /// <summary>A heavy door starts slowly, swings, then slows to a stop.</summary>
        public static float Ease(float amount)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
        }

        /// <summary>
        /// Turns about the vertical axis for the left leaf (hinged at -X) and the right leaf (hinged at +X).
        /// Both swing to the same side, so they open as a pair.
        /// </summary>
        public static (float left, float right) LeafAngles(int side, float amount, float openAngle)
        {
            float angle = Ease(amount) * openAngle * side;
            return (-angle, angle);
        }

        /// <summary>
        /// True if closing the door now would sweep a leaf through the player: they stand in the doorway,
        /// or within a leaf's length of a hinge on the side the leaves are open to.
        /// </summary>
        /// <param name="hingeX">How far each hinge is from the centre, which is also each leaf's length.</param>
        /// <param name="clearance">The player's radius, plus a margin.</param>
        public static bool BlocksClosing(Vector3 localPlayer, float hingeX, int openSide, float clearance)
        {
            bool inDoorway = Mathf.Abs(localPlayer.x) < hingeX + clearance
                && Mathf.Abs(localPlayer.z) < DoorwayHalfDepth + clearance;
            if (inDoorway) return true;

            if (localPlayer.z * openSide < -clearance) return false;

            var flat = new Vector2(localPlayer.x, localPlayer.z);
            float reach = hingeX + clearance;
            return Vector2.Distance(flat, new Vector2(-hingeX, 0f)) < reach
                || Vector2.Distance(flat, new Vector2(hingeX, 0f)) < reach;
        }
    }
}
