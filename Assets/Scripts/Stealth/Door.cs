using System.Collections.Generic;
using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// A double door the player opens and closes with Interact. The leaves swing away from the player,
    /// so they never sweep through them. A closed door blocks sight; monsters are kept out by the NavMesh, not by this.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Door : MonoBehaviour
    {
        static readonly List<Door> Active = new List<Door>();

        [Header("References")]
        [Tooltip("Pivots on the hinge line. The left leaf hangs at -X and the right at +X, in this object's space.")]
        [SerializeField] Transform leftLeaf;
        [SerializeField] Transform rightLeaf;
        [SerializeField] AudioClip creak;
        [SerializeField] AudioClip shut;

        [Header("Swing")]
        [SerializeField] float openAngle = 90f;
        [SerializeField] float openSeconds = 1.2f;

        [Header("Use")]
        [Tooltip("How close the player must be to the middle of the doorway, along the ground, in metres.")]
        [SerializeField] float reach = 2.4f;
        [Tooltip("The player's radius plus a margin. Closing waits until the leaves would miss them by this much.")]
        [SerializeField] float playerClearance = 0.45f;
        [Tooltip("How far the creak carries to monsters, in metres. 0 is silent to them.")]
        [SerializeField] float noiseRadius = 0f;

        [Header("Close behind the player")]
        [Tooltip("Swings shut on its own after the player comes through to the inside and is clear of the leaves. They can open it again.")]
        [SerializeField] bool closeBehindPlayer;
        [Tooltip("Which side is inside: 1 is the way this object's blue arrow points, -1 the other way.")]
        [SerializeField] int insideSide = 1;
        [Tooltip("How long it waits, once the player is through and clear, before it starts to close, in seconds.")]
        [SerializeField] float closeDelay = 0.4f;

        AudioSource voice;
        float amount;
        float target;
        int side = 1;
        bool knowsPlayer;
        Vector3 lastPlayerLocal;
        bool closingBehind;
        float closeWait;

        public bool IsOpen => target > 0f;
        public bool IsMoving => !Mathf.Approximately(amount, target);

        float HingeX => Mathf.Abs(leftLeaf.localPosition.x);

        void Awake() => voice = GetComponent<AudioSource>();
        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        /// <returns>The closest door the point can reach, or null if there is none.</returns>
        public static Door NearestInReach(Vector3 point)
        {
            Door nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var door in Active)
            {
                if (!LevelRules.SameLevel(door.transform.position, point)) continue;

                Vector3 offset = door.transform.position - point;
                offset.y = 0f;
                float distance = offset.magnitude;

                if (distance <= door.reach && distance < nearestDistance)
                {
                    nearest = door;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        /// <summary>False while the door is swinging, or if closing it now would hit the player.</summary>
        public bool CanToggle(Vector3 playerPosition)
        {
            if (IsMoving) return false;
            if (!IsOpen) return true;
            return !DoorRules.BlocksClosing(transform.InverseTransformPoint(playerPosition), HingeX, side, playerClearance);
        }

        public void Toggle(Vector3 playerPosition)
        {
            if (!CanToggle(playerPosition)) return;

            if (IsOpen)
            {
                target = 0f;
            }
            else
            {
                side = DoorRules.SwingSide(transform.InverseTransformPoint(playerPosition));
                target = 1f;
            }

            voice.PlayOneShot(creak);
            if (noiseRadius > 0f) Noise.Emit(transform.position, noiseRadius);
        }

        void Update()
        {
            if (closeBehindPlayer) CloseBehindPlayer();
            if (!IsMoving) return;

            amount = Mathf.MoveTowards(amount, target, Time.deltaTime / openSeconds);
            var (left, right) = DoorRules.LeafAngles(side, amount, openAngle);
            leftLeaf.localRotation = Quaternion.Euler(0f, left, 0f);
            rightLeaf.localRotation = Quaternion.Euler(0f, right, 0f);

            if (!IsMoving && !IsOpen) voice.PlayOneShot(shut);
        }

        // Waits for the player to come in through the open door, then shuts it behind them once the leaves would miss them.
        void CloseBehindPlayer()
        {
            PlayerController player = PlayerController.Current;
            if (player == null) return;

            Vector3 playerPosition = player.transform.position;
            Vector3 local = transform.InverseTransformPoint(playerPosition);
            if (knowsPlayer && IsOpen && DoorRules.CameInside(lastPlayerLocal, local, insideSide, reach))
            {
                closingBehind = true;
                closeWait = closeDelay;
            }
            lastPlayerLocal = local;
            knowsPlayer = true;

            if (!closingBehind) return;
            // Gone back out, or shut by hand: nothing to do.
            if (!IsOpen || local.z * insideSide < 0f)
            {
                closingBehind = false;
                return;
            }
            if (!CanToggle(playerPosition)) return;

            closeWait -= Time.deltaTime;
            if (closeWait > 0f) return;

            closingBehind = false;
            Toggle(playerPosition);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, reach);
        }
    }
}
