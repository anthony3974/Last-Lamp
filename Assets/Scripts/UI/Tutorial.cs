using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LastLamp
{
    /// <summary>
    /// Shows the <see cref="TutorialPrompt"/> children of this object on the HUD, one at a time and each once per
    /// launch of the game, so restarting the level or getting caught doesn't repeat them. Keys come from the input
    /// bindings, so the text stays right if a binding changes.
    /// </summary>
    public class Tutorial : MonoBehaviour
    {
        static readonly HashSet<string> SeenThisLaunch = new HashSet<string>();

        [Header("References")]
        [SerializeField] InputActionAsset actions;
        [SerializeField] PlayerController player;
        [SerializeField] PlayerCombat combat;
        [SerializeField] PlayerHiding hiding;
        [SerializeField] PlayerLives lives;
        [SerializeField] PlayerFlashlight flashlight;
        [SerializeField] GameObject panel;
        [SerializeField] Text label;

        [Header("Timing (seconds)")]
        [Tooltip("Each prompt stays up at least this long, even when its task is already done.")]
        [SerializeField] float minSeconds = 2f;
        [Tooltip("A prompt goes once the player has been away from its spot this long.")]
        [SerializeField] float awaySeconds = 3f;

        [Tooltip("The control scheme whose keys are shown.")]
        [SerializeField] string controlScheme = "Keyboard&Mouse";

        TutorialPrompt[] prompts;
        readonly Dictionary<string, TutorialPrompt> promptById = new Dictionary<string, TutorialPrompt>();
        TutorialQueue queue;
        Checkpoint startCheckpoint;
        bool noticed;
        bool caught;

        // Progress on the prompt now showing, counted from when it appeared.
        float walked;
        float sprinted;
        float crouchWalked;
        float brightFor;
        bool jumped;
        bool dodged;
        bool punched;
        bool wasDodging;

        // Statics survive between play sessions when domain reload is off, so start each launch with none seen.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSeen() => SeenThisLaunch.Clear();

        void Awake()
        {
            prompts = GetComponentsInChildren<TutorialPrompt>();
            foreach (var prompt in prompts) promptById[Id(prompt)] = prompt;
            queue = new TutorialQueue(SeenThisLaunch, minSeconds, awaySeconds);
        }

        void OnEnable()
        {
            player.Jumped += OnJumped;
            combat.Punched += OnPunched;
            lives.Caught += OnCaught;
        }

        void OnDisable()
        {
            player.Jumped -= OnJumped;
            combat.Punched -= OnPunched;
            lives.Caught -= OnCaught;
        }

        void Start()
        {
            startCheckpoint = Checkpoint.At(player.transform.position);
            Show(null);
        }

        void OnJumped() => jumped = true;

        void OnPunched() => punched = true;

        void OnCaught(bool outOfLives) => caught = true;

        void Update()
        {
            Vector3 position = player.transform.position;
            noticed |= Enemy.AnyNoticing();

            foreach (var prompt in prompts)
            {
                if (Wants(prompt, position)) queue.Want(Id(prompt), prompt.IsUrgent);
                else if (!prompt.IsUrgent) queue.Drop(Id(prompt));
            }

            TrackProgress();

            TutorialPrompt current = CurrentPrompt();
            bool done = current != null && IsDone(current);
            bool here = current == null || StillHere(current, position);
            if (queue.Tick(Time.deltaTime, done, here)) Show(CurrentPrompt());
        }

        bool Wants(TutorialPrompt prompt, Vector3 position)
        {
            switch (prompt.ShowWhen)
            {
                case TutorialTrigger.Area: return prompt.AreaContains(position);
                case TutorialTrigger.InLampPool: return !player.IsHidden && LampZone.AnyContains(position);
                case TutorialTrigger.HidingSpotInReach: return hiding.SpotInReach || hiding.IsHidden;
                case TutorialTrigger.NewCheckpoint:
                    Checkpoint reached = Checkpoint.At(position);
                    return reached != null && reached != startCheckpoint;
                case TutorialTrigger.MonsterNoticed: return noticed;
                case TutorialTrigger.Caught: return caught;
                default: return false;
            }
        }

        // Messages about something that happened, not a place, stay with the player wherever they go.
        bool StillHere(TutorialPrompt prompt, Vector3 position)
        {
            return prompt.IsUrgent || prompt.ShowWhen == TutorialTrigger.NewCheckpoint || Wants(prompt, position);
        }

        void TrackProgress()
        {
            float step = player.HorizontalSpeed * Time.deltaTime;
            walked += step;
            if (player.IsSprinting && player.HorizontalSpeed > 0.5f) sprinted += Time.deltaTime;
            if (player.IsCrouching) crouchWalked += step;
            if (flashlight.IsBright) brightFor += Time.deltaTime;
            if (player.IsDodging && !wasDodging) dodged = true;
            wasDodging = player.IsDodging;
        }

        bool IsDone(TutorialPrompt prompt)
        {
            switch (prompt.HideWhen)
            {
                case TutorialDone.Timer: return queue.ShownFor >= prompt.Amount;
                case TutorialDone.Walked: return walked >= prompt.Amount;
                case TutorialDone.Sprinted: return sprinted >= prompt.Amount;
                case TutorialDone.Jumped: return jumped;
                case TutorialDone.CrouchWalked: return crouchWalked >= prompt.Amount;
                case TutorialDone.BrightFlashlight: return brightFor >= prompt.Amount;
                case TutorialDone.DodgedAndPunched: return dodged && punched;
                case TutorialDone.Hidden: return hiding.IsHidden;
                default: return true;
            }
        }

        void Show(TutorialPrompt prompt)
        {
            walked = sprinted = crouchWalked = brightFor = 0f;
            jumped = dodged = punched = false;

            panel.SetActive(prompt != null);
            if (prompt != null) label.text = TutorialRules.Fill(prompt.Text, KeyFor);
        }

        TutorialPrompt CurrentPrompt()
        {
            return queue.Current != null && promptById.TryGetValue(queue.Current, out var prompt) ? prompt : null;
        }

        static string Id(TutorialPrompt prompt) => prompt.gameObject.scene.name + "/" + prompt.name;

        /// <returns>The key or keys for an action in the control scheme, or null if it has none.</returns>
        string KeyFor(string actionName)
        {
            InputAction action = actions.FindAction(actionName);
            if (action == null) return null;

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite)
                {
                    var keyByDirection = new Dictionary<string, string>();
                    for (int part = i + 1; part < bindings.Count && bindings[part].isPartOfComposite; part++)
                    {
                        string direction = bindings[part].name;
                        if (InScheme(bindings[part]) && !keyByDirection.ContainsKey(direction))
                            keyByDirection[direction] = Display(action, part);
                    }
                    string joined = TutorialRules.JoinDirections(keyByDirection);
                    if (joined.Length > 0) return joined;
                }
                else if (!bindings[i].isPartOfComposite && InScheme(bindings[i]))
                {
                    return Display(action, i);
                }
            }
            return null;
        }

        bool InScheme(InputBinding binding)
        {
            if (string.IsNullOrEmpty(binding.groups)) return false;
            foreach (string group in binding.groups.Split(InputBinding.Separator))
            {
                if (group == controlScheme) return true;
            }
            return false;
        }

        static string Display(InputAction action, int bindingIndex)
        {
            string display = action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            return TutorialRules.FriendlyKey(display);
        }
    }
}
