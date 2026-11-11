using UnityEngine;
using UnityEngine.AI;

namespace LastLamp
{
    /// <summary>
    /// Feeds an enemy's movement and state to the Wick's Animator. The animation never moves the enemy: its NavMeshAgent does.
    /// Also bends the Wick's spine forward while it walks, because the walk clip is upright and the Wick is hunched. The idle
    /// and the run are hunched already.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class WickAnimator : MonoBehaviour
    {
        static readonly int Speed = Animator.StringToHash("Speed");
        static readonly int Attacking = Animator.StringToHash("Attacking");
        static readonly int Stunned = Animator.StringToHash("Stunned");

        static readonly HumanBodyBones[] SpineBones = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };

        const string LookAroundLayer = "Look Around";
        const float SpeedDamping = 0.1f;
        // The Animator blends from the idle into the walk, then from the walk into the run, between these speeds.
        const float WalkSpeed = 1.8f;
        const float RunSpeed = 2.8f;

        [Tooltip("Seconds for the head to start or stop looking around when a search begins or ends.")]
        [SerializeField] float lookAroundBlend = 0.5f;
        [Tooltip("How far the spine bends forward at walking pace, in degrees, shared over its three bones.")]
        [SerializeField] float hunch = 21f;
        [Tooltip("Seconds for the hunch to come or go when the Wick grabs.")]
        [SerializeField] float hunchBlend = 0.3f;

        Animator animator;
        Enemy enemy;
        NavMeshAgent agent;
        int lookLayer;
        float lookWeight;
        float hunchWeight;

        void Awake()
        {
            animator = GetComponent<Animator>();
            enemy = GetComponentInParent<Enemy>();
            agent = GetComponentInParent<NavMeshAgent>();
            lookLayer = animator.GetLayerIndex(LookAroundLayer);
        }

        void Update()
        {
            EnemyState state = enemy.State;
            animator.SetFloat(Speed, agent.velocity.magnitude, SpeedDamping, Time.deltaTime);
            animator.SetBool(Attacking, state == EnemyState.Attack);
            animator.SetBool(Stunned, state == EnemyState.Stunned);

            float lookTarget = state == EnemyState.Search ? 1f : 0f;
            lookWeight = Mathf.MoveTowards(lookWeight, lookTarget, Time.deltaTime / lookAroundBlend);
            animator.SetLayerWeight(lookLayer, lookWeight);

            float hunchTarget = state == EnemyState.Attack ? 0f : HunchFor(animator.GetFloat(Speed));
            hunchWeight = Mathf.MoveTowards(hunchWeight, hunchTarget, Time.deltaTime / hunchBlend);
        }

        void LateUpdate() => Bend(hunchWeight);

        internal static float HunchFor(float speed) =>
            Mathf.Min(Mathf.InverseLerp(0f, WalkSpeed, speed), 1f - Mathf.InverseLerp(WalkSpeed, RunSpeed, speed));

        /// <summary>Tips each spine bone forward on top of the animated pose, which the Animator writes again every frame.</summary>
        internal void Bend(float weight)
        {
            if (animator == null) animator = GetComponent<Animator>();
            Quaternion tip = Quaternion.AngleAxis(hunch * weight / SpineBones.Length, transform.right);
            foreach (HumanBodyBones bone in SpineBones)
            {
                Transform spine = animator.GetBoneTransform(bone);
                if (spine != null) spine.rotation = tip * spine.rotation;
            }
        }
    }
}
