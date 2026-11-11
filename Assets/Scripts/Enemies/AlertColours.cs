using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// The starting colour of an enemy's alert light in each state. Enemy uses these as its defaults, and each enemy can
    /// still be tuned in the Inspector.
    /// </summary>
    public static class AlertColours
    {
        public static readonly Color Patrol = new Color(0.3f, 0.3f, 0.3f);
        public static readonly Color Suspicious = new Color(5f, 3.5f, 0f);
        public static readonly Color Investigate = new Color(5f, 1.5f, 0f);
        public static readonly Color Chase = new Color(8f, 0f, 0f);
        public static readonly Color Search = new Color(0.8f, 0.8f, 4f);
        public static readonly Color Attack = new Color(10f, 10f, 10f);
        public static readonly Color Stunned = new Color(0f, 4f, 0.5f);

        public static Color For(EnemyState state)
        {
            switch (state)
            {
                case EnemyState.Suspicious: return Suspicious;
                case EnemyState.Investigate: return Investigate;
                case EnemyState.Chase: return Chase;
                case EnemyState.Search: return Search;
                case EnemyState.Attack: return Attack;
                case EnemyState.Stunned: return Stunned;
                default: return Patrol;
            }
        }
    }
}
