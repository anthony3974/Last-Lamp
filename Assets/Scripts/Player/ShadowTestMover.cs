using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// Temporary, for PLAN.md task 5: walks the test capsule up and down the corridor.
    /// Delete when the real player arrives in task 6.
    /// </summary>
    public class ShadowTestMover : MonoBehaviour
    {
        [SerializeField] float startZ = 2f;
        [SerializeField] float endZ = 58f;
        [SerializeField] float speed = 2.5f;

        void Update()
        {
            var position = transform.position;
            position.z = startZ + Mathf.PingPong(Time.time * speed, endZ - startZ);
            transform.position = position;
        }
    }
}
