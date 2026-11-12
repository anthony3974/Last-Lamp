using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace LastLamp
{
    public enum PunchResult
    {
        /// <summary>No enemy was in reach.</summary>
        Missed,
        /// <summary>It stunned an enemy.</summary>
        Stunned,
        /// <summary>It reached only enemies stunned too recently, which shrugged it off.</summary>
        ShruggedOff,
    }

    /// <summary>
    /// Gives an EnemyBrain its senses and carries out its decisions with a NavMeshAgent.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(AudioSource))]
    public class Enemy : MonoBehaviour
    {
        static readonly List<Enemy> Active = new List<Enemy>();

        const float ArriveMargin = 0.3f;
        const float NewDestinationDistance = 0.3f;
        const string LitArea = "Lit";

        [Header("References")]
        [SerializeField] Transform[] patrolRoute;
        [SerializeField] Renderer alertLight;
        [SerializeField] AudioClip suspiciousSound;
        [SerializeField] AudioClip spottedSound;

        [Header("Sight")]
        [SerializeField] LayerMask visionBlockers;
        [SerializeField] float sightRange = 16f;
        [SerializeField] float sightHalfAngle = 55f;
        [SerializeField] float eyeHeight = 1.7f;
        [SerializeField] float turnSpeed = 220f;

        [Header("Patrol")]
        [Tooltip("Seconds the enemy waits at each end of its route.")]
        [SerializeField] float routeEndPause = 0f;

        [Header("Speed (metres per second)")]
        [SerializeField] float patrolSpeed = 1.8f;
        [SerializeField] float investigateSpeed = 2.8f;
        [SerializeField] float chaseSpeed = 4.8f;
        [SerializeField] float searchSpeed = 2.2f;

        [Header("Catching (metres)")]
        [SerializeField] float catchReach = 1.4f;
        [Tooltip("Reach to a player inside a hiding spot, measured to the middle of the spot.")]
        [SerializeField] float hiddenCatchReach = 2.5f;

        [Header("Search")]
        [Tooltip("How far from where it lost the player the enemy looks around, in metres.")]
        [SerializeField] float searchRadius = 4f;

        [Header("Alert Light")]
        [ColorUsage(false, true)] [SerializeField] Color patrolColour = AlertColours.Patrol;
        [ColorUsage(false, true)] [SerializeField] Color suspiciousColour = AlertColours.Suspicious;
        [ColorUsage(false, true)] [SerializeField] Color investigateColour = AlertColours.Investigate;
        [ColorUsage(false, true)] [SerializeField] Color chaseColour = AlertColours.Chase;
        [ColorUsage(false, true)] [SerializeField] Color searchColour = AlertColours.Search;
        [ColorUsage(false, true)] [SerializeField] Color attackColour = AlertColours.Attack;
        [ColorUsage(false, true)] [SerializeField] Color stunnedColour = AlertColours.Stunned;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly EnemyBrain brain = new EnemyBrain();

        // The player is in the Main scene, so it is found when the monster wakes rather than linked.
        PlayerController player;
        PlayerVisibility visibility;
        PlayerLives lives;
        NavMeshAgent agent;
        AudioSource voice;
        MaterialPropertyBlock alertBlock;
        Vector3 currentDestination;
        bool hasDestination;
        bool heardNoise;
        Vector3 noisePosition;
        Vector3 searchPoint;
        int litAreaMask;
        int routeIndex;
        int routeDirection = 1;
        float pauseLeft;
        Vector3 startPosition;
        Quaternion startRotation;

        public EnemyState State => brain.State;

        void Awake()
        {
            player = PlayerController.Current;
            visibility = player.GetComponent<PlayerVisibility>();
            lives = player.GetComponent<PlayerLives>();
            agent = GetComponent<NavMeshAgent>();
            voice = GetComponent<AudioSource>();
            alertBlock = new MaterialPropertyBlock();
            litAreaMask = 1 << NavMesh.GetAreaFromName(LitArea);
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        void OnEnable()
        {
            Active.Add(this);
            Noise.Emitted += Hear;
            lives.Caught += OnPlayerCaught;
        }

        void OnDisable()
        {
            Active.Remove(this);
            Noise.Emitted -= Hear;
            lives.Caught -= OnPlayerCaught;
        }

        // Every catch sends the player back to a checkpoint, so every monster starts over too:
        // back where it stood when the level began, at the start of its route, with no memory of the player.
        void OnPlayerCaught(bool outOfLives)
        {
            brain.Reset();
            heardNoise = false;
            routeIndex = 0;
            routeDirection = 1;
            pauseLeft = 0f;
            hasDestination = false;

            agent.Warp(startPosition);
            agent.ResetPath();
            agent.isStopped = false;
            transform.rotation = startRotation;
            agent.areaMask = NavMesh.AllAreas & ~litAreaMask;
            ShowAlertColour();
        }

        void Start() => ShowAlertColour();

        /// <returns>Stunned if the punch stunned any enemy, ShruggedOff if it reached only enemies stunned too recently, else Missed.</returns>
        public static PunchResult PunchNearby(Vector3 from, Vector3 direction, float reach, float halfAngle)
        {
            var result = PunchResult.Missed;
            foreach (var enemy in Active)
            {
                if (!CombatRules.InPunchArc(from, direction, enemy.transform.position, reach, halfAngle)) continue;
                if (enemy.TakePunch(from)) result = PunchResult.Stunned;
                else if (result == PunchResult.Missed) result = PunchResult.ShruggedOff;
            }
            return result;
        }

        /// <summary>True while any enemy is in its warning beat or investigating, before a chase.</summary>
        public static bool AnyNoticing()
        {
            foreach (var enemy in Active)
            {
                if (enemy.State == EnemyState.Suspicious || enemy.State == EnemyState.Investigate) return true;
            }
            return false;
        }

        /// <returns>True if the punch stunned this enemy, false if it was stunned too recently and shrugged it off.</returns>
        bool TakePunch(Vector3 playerPosition)
        {
            EnemyState before = brain.State;
            if (!brain.TryStun(playerPosition)) return false;
            OnStateChanged(before);
            return true;
        }

        void Hear(NoiseEvent noise)
        {
            if (Vector3.Distance(noise.Position, transform.position) > noise.Radius) return;

            heardNoise = true;
            noisePosition = noise.Position;
        }

        void Update()
        {
            EnemyState before = brain.State;
            brain.Tick(ReadSenses(), Time.deltaTime);
            heardNoise = false;

            if (brain.State != before) OnStateChanged(before);
            if (brain.CaughtPlayer) lives.Catch();
            UpdateLightAccess();
            Act();
        }

        EnemySenses ReadSenses()
        {
            Vector3 playerPosition = player.transform.position;
            return new EnemySenses
            {
                SeesPlayer = CanSeePlayer(),
                Exposure = visibility.Value,
                DistanceFraction = Vector3.Distance(transform.position, playerPosition) / sightRange,
                PlayerPosition = playerPosition,
                PlayerHidden = player.IsHidden,
                PlayerInReach = PlayerInReach(),
                PlayerDodging = player.IsDodging,
                HeardNoise = heardNoise,
                NoisePosition = noisePosition,
                Arrived = HasArrived(),
            };
        }

        bool CanSeePlayer()
        {
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            float targetHeight = player.IsCrouching ? 0.6f : 1.2f;
            Vector3 toPlayer = player.transform.position + Vector3.up * targetHeight - eye;
            float distance = toPlayer.magnitude;

            if (distance > sightRange) return false;
            if (Vector3.Angle(transform.forward, toPlayer) > sightHalfAngle) return false;
            return !Physics.Raycast(eye, toPlayer / distance, distance, visionBlockers, QueryTriggerInteraction.Ignore);
        }

        bool PlayerInReach()
        {
            if (!LevelRules.SameLevel(transform.position, player.transform.position)) return false;

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude <= (player.IsHidden ? hiddenCatchReach : catchReach);
        }

        bool HasArrived()
        {
            return hasDestination && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + ArriveMargin;
        }

        void OnStateChanged(EnemyState previous)
        {
            hasDestination = false;
            pauseLeft = 0f;
            searchPoint = brain.LastKnownPosition;
            ShowAlertColour();

            // Going back to a chase after a missed grab or a stun isn't a new sighting, so it stays quiet.
            bool newChase = brain.State == EnemyState.Chase && previous != EnemyState.Attack && previous != EnemyState.Stunned;

            if (brain.State == EnemyState.Suspicious) voice.PlayOneShot(suspiciousSound);
            else if (newChase || brain.State == EnemyState.Attack) voice.PlayOneShot(spottedSound);
        }

        void Act()
        {
            switch (brain.State)
            {
                case EnemyState.Patrol:
                    FollowRoute();
                    break;
                case EnemyState.Suspicious:
                    agent.isStopped = true;
                    FaceLastKnownPosition();
                    break;
                case EnemyState.Investigate:
                    MoveTo(brain.LastKnownPosition, investigateSpeed);
                    break;
                case EnemyState.Chase:
                    MoveTo(brain.LastKnownPosition, chaseSpeed);
                    break;
                case EnemyState.Search:
                    SearchArea();
                    break;
                case EnemyState.Attack:
                    agent.isStopped = true;
                    FaceLastKnownPosition();
                    break;
                case EnemyState.Stunned:
                    agent.isStopped = true;
                    break;
            }
        }

        // Only an enemy that is hunting may enter light. One already standing in light may stay on it, so it can walk out.
        void UpdateLightAccess()
        {
            bool standingInLight = NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)
                && (hit.mask & litAreaMask) != 0;

            agent.areaMask = brain.IsHunting || standingInLight ? NavMesh.AllAreas : NavMesh.AllAreas & ~litAreaMask;
        }

        void SearchArea()
        {
            if (HasArrived()) PickSearchPoint();
            MoveTo(searchPoint, searchSpeed);
        }

        void PickSearchPoint()
        {
            Vector2 offset = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = brain.LastKnownPosition + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, agent.areaMask)) searchPoint = hit.position;
        }

        void FollowRoute()
        {
            if (patrolRoute.Length == 0) return;

            if (pauseLeft > 0f) pauseLeft -= Time.deltaTime;
            else if (HasArrived()) AdvanceRoute();

            if (pauseLeft > 0f) agent.isStopped = true;
            else MoveTo(patrolRoute[routeIndex].position, patrolSpeed);
        }

        void AdvanceRoute()
        {
            if (routeIndex + routeDirection < 0 || routeIndex + routeDirection >= patrolRoute.Length)
            {
                routeDirection = -routeDirection;
                pauseLeft = routeEndPause;
            }
            routeIndex = Mathf.Clamp(routeIndex + routeDirection, 0, patrolRoute.Length - 1);
        }

        void MoveTo(Vector3 destination, float speed)
        {
            agent.isStopped = false;
            agent.speed = speed;

            if (hasDestination && Vector3.Distance(destination, currentDestination) < NewDestinationDistance) return;
            agent.SetDestination(destination);
            currentDestination = destination;
            hasDestination = true;
        }

        void FaceLastKnownPosition()
        {
            Vector3 direction = brain.LastKnownPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;

            var facing = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * Time.deltaTime);
        }

        void ShowAlertColour()
        {
            alertBlock.SetColor(BaseColor, ColourFor(brain.State));
            alertLight.SetPropertyBlock(alertBlock);
        }

        Color ColourFor(EnemyState state)
        {
            switch (state)
            {
                case EnemyState.Suspicious: return suspiciousColour;
                case EnemyState.Investigate: return investigateColour;
                case EnemyState.Chase: return chaseColour;
                case EnemyState.Search: return searchColour;
                case EnemyState.Attack: return attackColour;
                case EnemyState.Stunned: return stunnedColour;
                default: return patrolColour;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * eyeHeight, sightRange);
        }
    }
}
