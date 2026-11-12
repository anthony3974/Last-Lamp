using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace LastLamp
{
    /// <summary>
    /// Lives in the Main scene, which holds what every level shares: the player, the camera, the HUD and the night's
    /// light and sky. Each level is a scene of its own (see <see cref="Level"/>), loaded in the background alongside Main,
    /// so a level can be edited without any risk to the others.
    ///
    /// A new level (the game starting, the corridor's end, a restart) replaces every loaded level behind a fade to black,
    /// with the player on its start and fresh lives.
    ///
    /// Areas joined into one world stream instead: standing in an <see cref="AreaLink"/> loads the area on the other
    /// side in the background, and an area the player has left unloads once they are out of every link to it. At most
    /// two are loaded at once, the one the player is in and the next.
    /// </summary>
    public class LevelLoader : MonoBehaviour
    {
        /// <summary>Set by the editor when Play is pressed: the open level scenes, by path, to start in instead of the first level.</summary>
        public const string EditorLevelsKey = "LastLamp.PlayLevels";

        public static LevelLoader Current { get; private set; }

        [Header("References")]
        [SerializeField] PlayerController player;
        [SerializeField] PlayerLives lives;
        [SerializeField] CinemachineCamera followCamera;
        [SerializeField] Hud hud;

        [Header("Levels")]
        [Tooltip("The level the game starts in, by scene name. It must be in the build list.")]
        [SerializeField] string firstLevel = "Corridor_01";
        [Tooltip("How long the screen takes to fade to black, or back, when a new level loads, in real seconds.")]
        [SerializeField] float fadeSeconds = 0.4f;

        [Header("Joined areas")]
        [Tooltip("Most areas loaded at once: the one the player is in and the next one.")]
        [SerializeField] int maxAreas = 2;

        // Scenes this has loaded or is loading, by name, those still loading or unloading, and those that couldn't load.
        readonly List<string> loaded = new List<string>();
        readonly HashSet<string> busy = new HashSet<string>();
        readonly HashSet<string> missing = new HashSet<string>();
        readonly List<string> wanted = new List<string>();

        Level area;
        string startingLevel;
        bool switching;

        /// <summary>True while a new level is replacing the old one, behind the fade.</summary>
        public bool IsSwitching => switching;

        void Awake()
        {
            Current = this;
            // Nothing moves until the first level is there to stand on.
            Time.timeScale = 0f;
            hud.Fade.alpha = 1f;
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void Start()
        {
            var levels = new List<string>();
#if UNITY_EDITOR
            // Pressing Play with a level open starts in that level (the active one first), as if it were the first.
            foreach (string path in SessionState.GetString(EditorLevelsKey, "").Split(';'))
            {
                if (path.Length > 0) levels.Add(path);
            }
#endif
            if (levels.Count == 0) levels.Add(firstLevel);
            StartCoroutine(SwitchTo(levels));
        }

        /// <summary>
        /// Replaces every loaded level with this one, behind a fade to black, and starts the player on it with fresh lives.
        /// The game is frozen until it is in.
        /// </summary>
        public void LoadLevel(string level)
        {
            if (switching) return;
            StartCoroutine(SwitchTo(new List<string> { level }));
        }

        IEnumerator SwitchTo(List<string> levels)
        {
            switching = true;
            Time.timeScale = 0f;
            yield return Fade(1f);
            hud.HideFinished();

            ThreadPriority priority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.High;

            while (busy.Count > 0) yield return null;
            area = null;
            while (loaded.Count > 0) yield return Unload(loaded[loaded.Count - 1]);
            yield return Resources.UnloadUnusedAssets();

            // The player is put on the first level's start the moment it is in, before its objects Start (OnSceneLoaded).
            startingLevel = SceneName(levels[0]);
            foreach (string level in levels) yield return Load(level);
            if (area == null) StartOn(Level.In(SceneManager.GetSceneByName(startingLevel)));
            startingLevel = null;

            Application.backgroundLoadingPriority = priority;
            if (area == null) Debug.LogError($"Level Loader: {levels[0]} didn't load or has no Level object, so the player has nowhere to start.");

            // One frame for the new level's objects to start before the game moves.
            yield return null;
            Time.timeScale = 1f;
            switching = false;
            yield return Fade(0f);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == startingLevel) StartOn(Level.In(scene));
        }

        void StartOn(Level level)
        {
            if (level == null) return;

            area = level;
            player.StartAt(level.StartPosition, level.StartRotation);
            lives.StartLevel(level.FirstCheckpoint);
            // No camera damping across the jump: it starts behind the player.
            followCamera.PreviousStateIsValid = false;
        }

        void Update()
        {
            if (switching || area == null) return;

            Vector3 position = player.transform.position;
            Level here = Level.At(position, area);
            if (here != area)
            {
                area = here;
                lives.EnterArea(here.FirstCheckpoint);
            }

            wanted.Clear();
            wanted.Add(area.SceneName);
            AreaLink.AreasAt(position, wanted, maxAreas);

            foreach (string scene in wanted)
            {
                if (!loaded.Contains(scene) && !missing.Contains(scene)) StartCoroutine(Load(scene));
            }
            for (int i = loaded.Count - 1; i >= 0; i--)
            {
                string scene = loaded[i];
                if (!wanted.Contains(scene) && !busy.Contains(scene)) StartCoroutine(Unload(scene));
            }
        }

        IEnumerator Load(string level)
        {
            string scene = SceneName(level);
            loaded.Add(scene);
            busy.Add(scene);

            AsyncOperation loading = LoadAsync(level);
            if (loading != null) yield return loading;
            else
            {
                loaded.Remove(scene);
                missing.Add(scene);
            }

            busy.Remove(scene);
        }

        IEnumerator Unload(string scene)
        {
            busy.Add(scene);
            AsyncOperation unloading = SceneManager.GetSceneByName(scene).isLoaded ? SceneManager.UnloadSceneAsync(scene) : null;
            if (unloading != null) yield return unloading;

            loaded.Remove(scene);
            busy.Remove(scene);
        }

        IEnumerator Fade(float to)
        {
            CanvasGroup fade = hud.Fade;
            while (!Mathf.Approximately(fade.alpha, to))
            {
                // Capped, so the long frame after a load doesn't skip the fade.
                float step = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f) / Mathf.Max(fadeSeconds, 0.01f);
                fade.alpha = Mathf.MoveTowards(fade.alpha, to, step);
                yield return null;
            }
        }

        static string SceneName(string level) => Path.GetFileNameWithoutExtension(level);

        static AsyncOperation LoadAsync(string level)
        {
            var additive = new LoadSceneParameters(LoadSceneMode.Additive);
#if UNITY_EDITOR
            // In the editor, a scene can be played before it is in the build list, with a warning.
            string path = level.EndsWith(".unity") ? level : FindScenePath(level);
            if (path == null)
            {
                Debug.LogError($"Level Loader: there is no scene called {level}.");
                return null;
            }
            if (SceneUtility.GetBuildIndexByScenePath(path) < 0)
                Debug.LogWarning($"Level Loader: {SceneName(path)} isn't in the build list, so a build can't load it. Add it in File > Build Profiles.");
            return EditorSceneManager.LoadSceneAsyncInPlayMode(path, additive);
#else
            return SceneManager.LoadSceneAsync(level, additive);
#endif
        }

#if UNITY_EDITOR
        static string FindScenePath(string name)
        {
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (SceneName(scene.path) == name) return scene.path;
            }
            foreach (string guid in AssetDatabase.FindAssets($"t:Scene {name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (SceneName(path) == name) return path;
            }
            return null;
        }
#endif
    }
}
