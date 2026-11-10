using System.Linq;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Moves the bones in the lower half of the coat so the legs don't come through it.
    /// Animation clips only drive the body, so each column of the skirt swings out here when a knee or shin reaches it.
    /// </summary>
    public class CoatSkirt : MonoBehaviour
    {
        const string BonePrefix = "CoatSkirt_";
        const string Skeleton = "mixamorig:";

        const float MaxUpperAngle = 65f;
        const float MaxLowerAngle = 45f;
        const float ClearanceAngle = 10f;
        // Clearance is measured from the outside of the leg, not its centre, in metres.
        const float LegRadius = 0.12f;
        // How much of the upper swing the lower part may give back, so the hem hangs instead of sticking out.
        const float HangBack = 0.6f;
        // Each column follows part of its neighbours, so the cloth doesn't crease between them.
        const float NeighbourShare = 0.6f;
        const float Response = 18f;

        class Column
        {
            public Transform Upper, Lower;
            public Quaternion RestUpper, RestLower;
            public Vector3 Outward, Axis, LowerAxis;
            public float RestUpperAngle, RestLowerAngle, UpperAngle, LowerAngle, Target;
        }

        Column[] columns;
        Transform hips, leftKnee, rightKnee, leftAnkle, rightAnkle;
        Vector3 down;

        void Awake() => Setup();

        void LateUpdate() => Apply(Time.deltaTime);

        void Setup()
        {
            Transform[] bones = GetComponentsInChildren<Transform>(true);
            Transform Bone(string name) => bones.First(bone => bone.name == Skeleton + name);

            hips = Bone("Hips");
            leftKnee = Bone("LeftLeg");
            rightKnee = Bone("RightLeg");
            leftAnkle = Bone("LeftFoot");
            rightAnkle = Bone("RightFoot");
            down = hips.InverseTransformDirection(Vector3.down).normalized;

            Vector3 middle = Vector3.zero;
            Transform[] uppers = bones.Where(bone => bone.name.StartsWith(BonePrefix) && bone.name.EndsWith("_Upper")).OrderBy(bone => bone.name).ToArray();
            foreach (Transform upper in uppers) middle += hips.InverseTransformPoint(upper.position);
            middle /= uppers.Length;

            columns = uppers.Select(upper => MakeColumn(upper, middle)).ToArray();
        }

        Column MakeColumn(Transform upper, Vector3 middle)
        {
            Transform lower = upper.GetChild(0);
            Vector3 top = hips.InverseTransformPoint(upper.position);
            Vector3 knee = hips.InverseTransformPoint(lower.position);
            Vector3 hem = hips.InverseTransformPoint(lower.TransformPoint(EndOfLower(lower)));
            Vector3 outward = Vector3.ProjectOnPlane(top - middle, down).normalized;
            Vector3 axis = Vector3.Cross(down, outward);
            return new Column
            {
                Upper = upper,
                Lower = lower,
                RestUpper = upper.localRotation,
                RestLower = lower.localRotation,
                Outward = outward,
                Axis = axis,
                LowerAxis = Quaternion.Inverse(upper.localRotation) * axis,
                RestUpperAngle = AngleFromDown(top, knee, down, outward),
                RestLowerAngle = AngleFromDown(knee, hem, down, outward),
            };
        }

        /// <summary>The lower bone has nothing after it, so its length comes from the same column's upper bone.</summary>
        static Vector3 EndOfLower(Transform lower) => lower.InverseTransformPoint(lower.position) + lower.localPosition.magnitude * Vector3.up;

        /// <summary>Applies the pose now. <paramref name="deltaTime"/> of 0 jumps straight to it.</summary>
        public void Apply(float deltaTime)
        {
            if (columns == null) Setup();

            foreach (Column column in columns)
            {
                Vector3 top = hips.InverseTransformPoint(column.Upper.position);
                column.Target = UpperSwing(Reach(top, leftKnee, rightKnee, column), column.RestUpperAngle, MaxUpperAngle);
            }

            float blend = deltaTime <= 0f ? 1f : 1f - Mathf.Exp(-Response * deltaTime);
            for (int i = 0; i < columns.Length; i++)
            {
                Column column = columns[i];
                float neighbour = Mathf.Max(columns[(i + columns.Length - 1) % columns.Length].Target, columns[(i + 1) % columns.Length].Target);
                column.UpperAngle = Mathf.Lerp(column.UpperAngle, Mathf.Max(column.Target, neighbour * NeighbourShare), blend);
                column.Upper.localRotation = Quaternion.AngleAxis(column.UpperAngle, column.Axis) * column.RestUpper;

                Vector3 knee = hips.InverseTransformPoint(column.Lower.position);
                float reach = Reach(knee, leftAnkle, rightAnkle, column);
                float target = LowerSwing(reach, column.RestLowerAngle, column.UpperAngle, MaxLowerAngle);
                column.LowerAngle = Mathf.Lerp(column.LowerAngle, target, blend);
                column.Lower.localRotation = Quaternion.AngleAxis(column.LowerAngle, column.LowerAxis) * column.RestLower;
            }
        }

        float Reach(Vector3 from, Transform left, Transform right, Column column)
        {
            Vector3 skin = column.Outward * (LegRadius / hips.lossyScale.x);
            return Mathf.Max(
                AngleFromDown(from, hips.InverseTransformPoint(left.position) + skin, down, column.Outward),
                AngleFromDown(from, hips.InverseTransformPoint(right.position) + skin, down, column.Outward));
        }

        /// <summary>How far a point is from straight down, tipped toward outward, in degrees. Inward is negative.</summary>
        internal static float AngleFromDown(Vector3 from, Vector3 to, Vector3 down, Vector3 outward)
        {
            Vector3 offset = to - from;
            return Mathf.Atan2(Vector3.Dot(offset, outward), Vector3.Dot(offset, down)) * Mathf.Rad2Deg;
        }

        internal static float UpperSwing(float reach, float restAngle, float maxAngle)
        {
            return Mathf.Clamp(reach + ClearanceAngle - restAngle, 0f, maxAngle);
        }

        internal static float LowerSwing(float reach, float restAngle, float upperAngle, float maxAngle)
        {
            return Mathf.Clamp(reach + ClearanceAngle - (restAngle + upperAngle), -upperAngle * HangBack, maxAngle);
        }
    }
}
