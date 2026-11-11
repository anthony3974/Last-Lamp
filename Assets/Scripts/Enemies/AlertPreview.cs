using UnityEngine;

namespace LastLamp
{
    /// <summary>
    /// For judging a monster model on its own, as in MonsterStage_01: steps its alert light through every enemy state
    /// in a loop, in the colours Enemy starts with.
    /// </summary>
    public class AlertPreview : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly EnemyState[] States = (EnemyState[])System.Enum.GetValues(typeof(EnemyState));

        [SerializeField] Renderer alertLight;
        [SerializeField] float secondsPerState = 2f;

        MaterialPropertyBlock block;
        int index = -1;
        float nextChange;

        void Awake() => block = new MaterialPropertyBlock();

        void Update()
        {
            if (Time.time < nextChange) return;

            nextChange = Time.time + secondsPerState;
            index = (index + 1) % States.Length;
            block.SetColor(BaseColor, AlertColours.For(States[index]));
            alertLight.SetPropertyBlock(block);
        }
    }
}
