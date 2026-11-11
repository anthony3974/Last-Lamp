using UnityEngine;

namespace LastLamp
{
    public enum TutorialTrigger
    {
        /// <summary>The player is inside this object's area.</summary>
        Area,
        /// <summary>The player is in any lit pool.</summary>
        InLampPool,
        /// <summary>A hiding spot is in reach.</summary>
        HidingSpotInReach,
        /// <summary>The player reaches a checkpoint other than the one they started on.</summary>
        NewCheckpoint,
        /// <summary>A monster gives its warning beat or starts investigating. Urgent.</summary>
        MonsterNoticed,
        /// <summary>The player is caught. Urgent.</summary>
        Caught,
    }

    public enum TutorialDone
    {
        /// <summary>After Amount seconds.</summary>
        Timer,
        /// <summary>After walking Amount metres.</summary>
        Walked,
        /// <summary>After sprinting for Amount seconds.</summary>
        Sprinted,
        Jumped,
        /// <summary>After crouch-walking Amount metres.</summary>
        CrouchWalked,
        /// <summary>After holding the bright flashlight for Amount seconds.</summary>
        BrightFlashlight,
        DodgedAndPunched,
        Hidden,
    }

    /// <summary>
    /// One tutorial message: what it says, when it shows and when it is done. <see cref="Tutorial"/> shows them in
    /// hierarchy order, one at a time, each once. Write {ActionName} in the text, such as {Sprint}, for that action's key.
    /// </summary>
    public class TutorialPrompt : MonoBehaviour
    {
        [TextArea(2, 4)]
        [SerializeField] string text;

        [SerializeField] TutorialTrigger showWhen;
        [Tooltip("Width (X) and depth (Z) of the area around this object, in metres. Only used when Show When is Area.")]
        [SerializeField] Vector2 areaSize = new Vector2(6f, 2f);

        [SerializeField] TutorialDone hideWhen;
        [Tooltip("Seconds for Timer, Sprinted and Bright Flashlight. Metres for Walked and Crouch Walked. Not used by the others.")]
        [SerializeField] float amount = 3f;

        public string Text => text;
        public TutorialTrigger ShowWhen => showWhen;
        public TutorialDone HideWhen => hideWhen;
        public float Amount => amount;
        public bool IsUrgent => showWhen == TutorialTrigger.MonsterNoticed || showWhen == TutorialTrigger.Caught;

        public bool AreaContains(Vector3 point)
        {
            if (!LevelRules.SameLevel(transform.position, point)) return false;
            Vector3 local = transform.InverseTransformPoint(point);
            return Mathf.Abs(local.x) <= areaSize.x * 0.5f && Mathf.Abs(local.z) <= areaSize.y * 0.5f;
        }

        void OnDrawGizmosSelected()
        {
            if (showWhen != TutorialTrigger.Area) return;
            Gizmos.color = new Color(0.4f, 0.8f, 1f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(areaSize.x, 0f, areaSize.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
