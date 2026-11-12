using UnityEngine;

namespace LastLamp
{
    public enum EnemyState
    {
        Patrol,
        Suspicious,
        Investigate,
        Chase,
        Search,
        Attack,
        Stunned,
    }

    internal struct EnemySenses
    {
        public bool SeesPlayer;
        public float Exposure;
        public float DistanceFraction;
        public Vector3 PlayerPosition;
        public bool PlayerHidden;
        public bool PlayerInReach;
        public bool PlayerDodging;
        public bool HeardNoise;
        public Vector3 NoisePosition;
        public bool Arrived;
    }

    /// <summary>
    /// Decides what an enemy is doing. It knows nothing about Unity components, so it can be tested on its own.
    /// </summary>
    internal sealed class EnemyBrain
    {
        public const float SuspiciousSeconds = 1.2f;
        public const float LoseSightSeconds = 2.5f;
        public const float SearchSeconds = 4f;
        public const float HiddenSearchSeconds = 10f;
        public const float AttackWindupSeconds = 0.6f;
        public const float StunSeconds = 2.5f;
        public const float StunImmunitySeconds = 6f;

        readonly DetectionMeter meter = new DetectionMeter();
        float secondsInState;
        float secondsOutOfSight;
        float searchDuration = SearchSeconds;
        bool playerWasHidden;
        bool sawPlayerLastTick;
        bool watchedPlayerHide;
        float secondsSinceStun = StunImmunitySeconds;

        public EnemyState State { get; private set; } = EnemyState.Patrol;
        public Vector3 LastKnownPosition { get; private set; }

        /// <summary>True for the one tick in which this enemy catches the player.</summary>
        public bool CaughtPlayer { get; private set; }

        /// <summary>While hunting, an enemy doesn't stop for lamp light.</summary>
        public bool IsHunting => State == EnemyState.Chase || State == EnemyState.Attack || State == EnemyState.Stunned;

        /// <returns>False if the enemy shrugs the punch off because it was stunned too recently.</returns>
        public bool TryStun(Vector3 playerPosition)
        {
            if (secondsSinceStun < StunImmunitySeconds) return false;

            secondsSinceStun = 0f;
            LastKnownPosition = playerPosition;
            Enter(EnemyState.Stunned);
            return true;
        }

        /// <summary>Forgets the player completely and goes back to patrolling, as at the start of the level.</summary>
        public void Reset()
        {
            meter.Reset();
            playerWasHidden = false;
            sawPlayerLastTick = false;
            watchedPlayerHide = false;
            secondsSinceStun = StunImmunitySeconds;
            CaughtPlayer = false;
            LastKnownPosition = Vector3.zero;
            Enter(EnemyState.Patrol);
        }

        public void Tick(in EnemySenses rawSenses, float deltaTime)
        {
            CaughtPlayer = false;
            secondsSinceStun += deltaTime;

            EnemySenses senses = ApplyHiding(rawSenses);
            meter.Tick(senses.SeesPlayer, senses.Exposure, senses.DistanceFraction, deltaTime);
            RememberPosition(senses);
            secondsInState += deltaTime;

            switch (State)
            {
                case EnemyState.Patrol: TickPatrol(senses); break;
                case EnemyState.Suspicious: TickSuspicious(); break;
                case EnemyState.Investigate: TickInvestigate(senses); break;
                case EnemyState.Chase: TickChase(senses, deltaTime); break;
                case EnemyState.Search: TickSearch(senses); break;
                case EnemyState.Attack: TickAttack(senses); break;
                case EnemyState.Stunned: TickStunned(); break;
            }
        }

        // A hidden player can't be seen, unless this enemy had already noticed them and was looking as they hid.
        // An enemy that watched them hide keeps treating them as in plain sight.
        EnemySenses ApplyHiding(EnemySenses senses)
        {
            if (senses.PlayerHidden && !playerWasHidden)
                watchedPlayerHide = sawPlayerLastTick && meter.Stage != DetectionStage.Unaware;
            playerWasHidden = senses.PlayerHidden;

            if (senses.PlayerHidden)
            {
                senses.SeesPlayer = watchedPlayerHide;
                senses.Exposure = watchedPlayerHide ? 1f : 0f;
            }

            sawPlayerLastTick = senses.SeesPlayer;
            return senses;
        }

        void RememberPosition(in EnemySenses senses)
        {
            // A stunned enemy is reeling, so it searches where it was punched, not where the player went.
            if (State == EnemyState.Stunned) return;

            if (senses.HeardNoise) LastKnownPosition = senses.NoisePosition;
            if (senses.SeesPlayer && (senses.Exposure > 0f || IsHunting)) LastKnownPosition = senses.PlayerPosition;
        }

        void TickPatrol(in EnemySenses senses)
        {
            if (meter.Stage != DetectionStage.Unaware || senses.HeardNoise) Enter(EnemyState.Suspicious);
        }

        void TickSuspicious()
        {
            if (meter.Stage == DetectionStage.Spotted) Enter(EnemyState.Chase);
            else if (secondsInState >= SuspiciousSeconds) Enter(EnemyState.Investigate);
        }

        void TickInvestigate(in EnemySenses senses)
        {
            if (meter.Stage == DetectionStage.Spotted) Enter(EnemyState.Chase);
            else if (senses.Arrived) Enter(EnemyState.Search);
        }

        void TickAttack(in EnemySenses senses)
        {
            if (secondsInState < AttackWindupSeconds) return;

            if (senses.PlayerInReach && senses.SeesPlayer && !senses.PlayerDodging)
            {
                CaughtPlayer = true;
                meter.Reset();
                Enter(EnemyState.Patrol);
            }
            else
            {
                Enter(EnemyState.Chase);
            }
        }

        // A punch always breaks a chase: the enemy recovers into Search around where it was punched,
        // and has to notice the player afresh, warning first, before it can chase again.
        void TickStunned()
        {
            if (secondsInState < StunSeconds) return;
            meter.Reset();
            Enter(EnemyState.Search);
        }

        void TickChase(in EnemySenses senses, float deltaTime)
        {
            if (senses.PlayerInReach && senses.SeesPlayer)
            {
                Enter(EnemyState.Attack);
                return;
            }

            secondsOutOfSight = senses.SeesPlayer ? 0f : secondsOutOfSight + deltaTime;

            bool reachedWhereItLostThem = senses.PlayerHidden && !senses.SeesPlayer && senses.Arrived;
            if (reachedWhereItLostThem || secondsOutOfSight >= LoseSightSeconds)
            {
                meter.Reset();
                Enter(EnemyState.Search);
            }
        }

        void TickSearch(in EnemySenses senses)
        {
            if (meter.Stage == DetectionStage.Spotted) Enter(EnemyState.Chase);
            else if (meter.Stage == DetectionStage.Suspicious || senses.HeardNoise) Enter(EnemyState.Suspicious);
            else if (secondsInState >= searchDuration)
            {
                meter.Reset();
                Enter(EnemyState.Patrol);
            }
        }

        void Enter(EnemyState next)
        {
            State = next;
            secondsInState = 0f;
            secondsOutOfSight = 0f;
            searchDuration = playerWasHidden ? HiddenSearchSeconds : SearchSeconds;
        }
    }
}
