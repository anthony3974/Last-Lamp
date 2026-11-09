using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// One punch, aimed where the player is looking. The dodge lives in PlayerController because it moves the player.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] float punchReach = 2f;
        [SerializeField] float punchHalfAngle = 75f;
        [SerializeField] float punchCooldown = 0.6f;

        PlayerController player;
        float cooldownLeft;

        void Awake()
        {
            player = GetComponent<PlayerController>();
        }

        void Update()
        {
            cooldownLeft = Mathf.Max(0f, cooldownLeft - Time.deltaTime);
            if (player.IsHidden || cooldownLeft > 0f || !player.AttackPressed) return;

            cooldownLeft = punchCooldown;
            Enemy.PunchNearby(transform.position, player.LookDirection, punchReach, punchHalfAngle);
        }
    }
}
