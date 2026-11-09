using UnityEngine;
using UnityEngine.AI;

namespace LastLamp
{
    /// <summary>
    /// Gives an EnemyBrain its senses and carries out its decisions with a NavMeshAgent.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(AudioSource))]
    public class Enemy : MonoBehaviour
    {
        const float ArriveMargin = 0.3f;
        const float NewDestinationDistance = 0.3f;
        const float ChaseRepathSeconds = 0.25f;

        [Header("References")]
        [SerializeField] PlayerController player;
        [SerializeField] PlayerVisibility visibility;
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

        [Header("Speed (metres per second)")]
        [SerializeField] float patrolSpeed = 1.8f;
        [SerializeField] float investigateSpeed = 2.8f;
        [SerializeField] float chaseSpeed = 4.8f;
        [SerializeField] float searchSpeed = 2.2f;

        [Header("Search")]
        [Tooltip("How far from where it lost the player the enemy looks around, in metres.")]
        [SerializeField] float searchRadius = 4f;

        [Header("Alert Light")]
        [ColorUsage(false, true)] [SerializeField] Color patrolColour = new Color(0.3f, 0.3f, 0.3f);
        [ColorUsage(false, true)] [SerializeField] Color suspiciousColour = new Color(5f, 3.5f, 0f);
        [ColorUsage(false, true)] [SerializeField] Color investigateColour = new Color(5f, 1.5f, 0f);
        [ColorUsage(false, true)] [SerializeField] Color chaseColour = new Color(8f, 0f, 0f);
        [ColorUsage(false, true)] [SerializeField] Color searchColour = new Color(0.8f, 0.8f, 4f);

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly EnemyBrain brain = new EnemyBrain();

        NavMeshAgent agent;
        AudioSource voice;
        MaterialPropertyBlock alertBlock;
        Vector3 currentDestination;
        bool hasDestination;
        bool heardNoise;
        Vector3 noisePosition;
        Vector3 searchPoint;
        float nextChaseRepath;
        int routeIndex;
        int routeDirection = 1;

        public EnemyState State => brain.State;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            voice = GetComponent<AudioSource>();
            alertBlock = new MaterialPropertyBlock();
        }

        void OnEnable() => Noise.Emitted += Hear;
        void OnDisable() => Noise.Emitted -= Hear;

        void Start() => ShowAlertColour();

        void Hear(NoiseEvent noise)
        {
            Vector3 offset = noise.Position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > noise.Radius) return;

            heardNoise = true;
            noisePosition = noise.Position;
        }

        void Update()
        {
            EnemyState before = brain.State;
            brain.Tick(ReadSenses(), Time.deltaTime);
            heardNoise = false;

            if (brain.State != before) OnStateChanged();
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

        bool HasArrived()
        {
            return hasDestination && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + ArriveMargin;
        }

        void OnStateChanged()
        {
            hasDestination = false;
            searchPoint = brain.LastKnownPosition;
            LampZone.SetOpenToEnemies(brain.State == EnemyState.Chase);
            ShowAlertColour();

            if (brain.State == EnemyState.Suspicious) voice.PlayOneShot(suspiciousSound);
            else if (brain.State == EnemyState.Chase) voice.PlayOneShot(spottedSound);
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
                    RepathDuringChase();
                    MoveTo(brain.LastKnownPosition, chaseSpeed);
                    break;
                case EnemyState.Search:
                    SearchArea();
                    break;
            }
        }

        // Lit pools only open a moment after a chase starts, so a path planned to the edge of one has to be replaced.
        void RepathDuringChase()
        {
            if (Time.time < nextChaseRepath) return;
            hasDestination = false;
            nextChaseRepath = Time.time + ChaseRepathSeconds;
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

            if (HasArrived())
            {
                if (routeIndex + routeDirection < 0 || routeIndex + routeDirection >= patrolRoute.Length)
                    routeDirection = -routeDirection;
                routeIndex = Mathf.Clamp(routeIndex + routeDirection, 0, patrolRoute.Length - 1);
            }

            MoveTo(patrolRoute[routeIndex].position, patrolSpeed);
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
