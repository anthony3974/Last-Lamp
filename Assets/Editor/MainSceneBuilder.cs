using System.IO;
using System.Linq;
using LastLamp;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool (task 29): makes the Main scene that every level loads alongside. Each level had its own copy of the
/// player, camera, light, volume and HUD. Main keeps the corridor's, with the warehouse's door use added, plus the Level
/// Loader and a fade. Corridor_01 and Warehouse_01 lose their copies and gain a Level object with the player's start
/// and first checkpoint. The build list becomes Main, Corridor_01, Warehouse_01. Delete once checked.
/// </summary>
public static class MainSceneBuilder
{
    const string MainPath = "Assets/Scenes/Main.unity";
    const string CorridorPath = "Assets/Scenes/Corridor_01.unity";
    const string WarehousePath = "Assets/Scenes/Warehouse_01.unity";

    // Every level had its own copy of these. Both copies match, apart from the player's place and the HUD's levels.
    static readonly string[] Shared =
    {
        "Main Camera", "Directional Light", "Global Volume", "Player", "Player Follow Camera", "HUD", "Noise Debug",
    };

    [MenuItem("Tools/Build Main Scene")]
    public static void Run()
    {
        if (File.Exists(MainPath))
        {
            Debug.LogError($"Build Main Scene: {MainPath} already exists, so this has already been run.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // A copy of the corridor, so Main's sky, ambient light and other scene settings match the levels exactly.
        if (!AssetDatabase.CopyAsset(CorridorPath, MainPath))
        {
            Debug.LogError("Build Main Scene: couldn't copy the corridor.");
            return;
        }

        BuildMain();
        BuildLevel(CorridorPath);
        BuildLevel(WarehousePath);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MainPath, true),
            new EditorBuildSettingsScene(CorridorPath, true),
            new EditorBuildSettingsScene(WarehousePath, true),
        };
        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        EditorSceneManager.OpenScene(CorridorPath, OpenSceneMode.Additive);
        Debug.Log("Build Main Scene: finished.");
    }

    static GameObject Root(Scene scene, string name) => scene.GetRootGameObjects().First(root => root.name == name);

    static void Set(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // --- Main ---

    static void BuildMain()
    {
        Scene scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!Shared.Contains(root.name)) Object.DestroyImmediate(root);
        }

        GameObject player = Root(scene, "Player");
        Transform hud = Root(scene, "HUD").transform;

        // Only the warehouse's player had door use. The one player now plays every level.
        var doors = player.AddComponent<PlayerDoors>();
        Set(hud.Find("Interact Prompt").GetComponent<InteractPrompt>(), "doors", doors);

        CanvasGroup fade = BuildFade(hud);

        var parts = hud.gameObject.AddComponent<Hud>();
        Set(parts, "finishedMessage", hud.Find("Finished Message").GetComponent<Text>());
        Set(parts, "tutorialPanel", hud.Find("Tutorial Panel").gameObject);
        Set(parts, "tutorialLabel", hud.Find("Tutorial Panel/Text").GetComponent<Text>());
        Set(parts, "fade", fade);

        var loaderObject = new GameObject("Level Loader");
        SceneManager.MoveGameObjectToScene(loaderObject, scene);
        var loader = loaderObject.AddComponent<LevelLoader>();
        Set(loader, "player", player.GetComponent<PlayerController>());
        Set(loader, "lives", player.GetComponent<PlayerLives>());
        Set(loader, "followCamera", Root(scene, "Player Follow Camera").GetComponent<CinemachineCamera>());
        Set(loader, "hud", parts);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // Black over the whole HUD, in front of everything. Clear in the scene, so it doesn't hide the Game view while
    // editing; the Level Loader turns it black as the game starts.
    static CanvasGroup BuildFade(Transform hud)
    {
        var fade = new GameObject("Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        fade.layer = hud.gameObject.layer;

        var rect = (RectTransform)fade.transform;
        rect.SetParent(hud, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        var image = fade.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        var group = fade.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    // --- Levels ---

    static void BuildLevel(string path)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        // Read what the level needs from its own copies before they go.
        Transform player = Root(scene, "Player").transform;
        Vector3 startPosition = player.position;
        Quaternion startRotation = player.rotation;
        string finishedText = Root(scene, "HUD").transform.Find("Finished Message").GetComponent<Text>().text;
        // Both levels' players start on Checkpoint_01, which is PlayerLives' first checkpoint in each.
        var first = Root(scene, "Checkpoints").transform.Find("Checkpoint_01").GetComponent<Checkpoint>();

        foreach (string name in Shared) Object.DestroyImmediate(Root(scene, name));

        var levelObject = new GameObject("Level");
        SceneManager.MoveGameObjectToScene(levelObject, scene);
        levelObject.transform.SetSiblingIndex(0);

        var start = new GameObject("Player Start").transform;
        start.SetParent(levelObject.transform, false);
        start.SetPositionAndRotation(startPosition, startRotation);

        var level = levelObject.AddComponent<Level>();
        Set(level, "playerStart", start);
        Set(level, "firstCheckpoint", first);

        Bounds area = AreaBounds(scene);
        var serialized = new SerializedObject(level);
        serialized.FindProperty("bounds").boundsValue = area;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        foreach (EndPoint end in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EndPoint>(true)))
        {
            var endPoint = new SerializedObject(end);
            endPoint.FindProperty("finishedText").stringValue = finishedText;
            endPoint.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Build Main Scene: {scene.name} starts the player at {startPosition} on {first.name}. " +
                  $"Area from {area.min} to {area.max}. Finished message: \"{finishedText}\".");
    }

    // Everything solid in the level, a metre wider on each side and 3 m higher, so a jump stays inside, to whole metres.
    static Bounds AreaBounds(Scene scene)
    {
        Collider[] solids = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Collider>())
            .Where(collider => collider.enabled && !collider.isTrigger)
            .ToArray();

        Physics.SyncTransforms();
        Bounds area = solids[0].bounds;
        foreach (Collider solid in solids) area.Encapsulate(solid.bounds);

        Vector3 min = area.min - new Vector3(1f, 0f, 1f);
        Vector3 max = area.max + new Vector3(1f, 3f, 1f);
        min = new Vector3(Mathf.Floor(min.x), Mathf.Floor(min.y), Mathf.Floor(min.z));
        max = new Vector3(Mathf.Ceil(max.x), Mathf.Ceil(max.y), Mathf.Ceil(max.z));

        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }
}
