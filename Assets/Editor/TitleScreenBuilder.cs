using System.Collections.Generic;
using System.Linq;
using LastLamp;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool: builds the title screen. The Title scene is its backdrop: a wet street at night against a brick wall,
/// one street lamp and the man standing under it, seen by a still camera.
/// In Main's HUD it adds the Title Menu (New Game, Continue and Quit, copies of the shared button), hooks it to the
/// Level Loader, and points the pause menu's Quit at it. Delete once checked.
/// </summary>
public static class TitleScreenBuilder
{
    const string MainPath = "Assets/Scenes/Main.unity";
    const string TitlePath = "Assets/Scenes/Title.unity";
    const string ButtonPath = "Assets/Prefabs/UIButton.prefab";

    // The HUD's text colour.
    static readonly Color TextColour = new Color(0.9f, 0.9f, 0.9f, 1f);
    // The parts of the HUD that only mean something while playing.
    static readonly string[] PlayOnly = { "Lives", "Visibility Cue", "Interact Prompt" };

    static Font font;

    [MenuItem("Tools/Build Title Screen")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TitlePath) != null)
        {
            Debug.LogError($"Build Title Screen: {TitlePath} already exists, so this has already been run.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        DimButtonLabels();
        BuildBackdrop();
        AddToBuildList();
        BuildMenu();

        AssetDatabase.SaveAssets();
        Debug.Log("Build Title Screen: done.");
    }

    // A button that can't be pressed, like Continue before there is a save, greys out its text too.
    static void DimButtonLabels()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ButtonPath);
        var dim = root.GetComponent<DimLabelWhenOff>() ?? root.AddComponent<DimLabelWhenOff>();
        Set(dim, "label", root.GetComponentInChildren<Text>());
        PrefabUtility.SaveAsPrefabAsset(root, ButtonPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // --- The Title scene ---

    static void BuildBackdrop()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var asphalt = Material("Assets/Materials/Warehouse_Asphalt.mat");
        var brick = Material("Assets/Materials/Warehouse_Brick.mat");
        var steel = Material("Assets/Materials/Warehouse_Rusted_Steel.mat");

        // The man stands here, under the lamp and right of the screen's middle, looking off into the dark on the left.
        var level = new GameObject("Level", typeof(Level));
        var start = new GameObject("Player Start").transform;
        start.SetParent(level.transform, false);
        start.localPosition = new Vector3(1.6f, 0f, 0.3f);
        start.localRotation = Quaternion.Euler(0f, 235f, 0f);
        Set(level.GetComponent<Level>(), "playerStart", start);

        Transform street = new GameObject("Street").transform;
        Box("Ground", street, new Vector3(0f, -0.1f, 4f), new Vector3(40f, 0.2f, 30f), asphalt);
        Box("Wall", street, new Vector3(0f, 4f, 2.6f), new Vector3(40f, 8f, 0.4f), brick);

        // One street lamp, the last one, just behind him.
        Transform post = new GameObject("Lamp_Post").transform;
        post.SetParent(street, false);
        post.localPosition = new Vector3(2.3f, 0f, 1.6f);
        Box("Pole", post, new Vector3(0f, 1.85f, 0f), new Vector3(0.15f, 3.7f, 0.15f), steel);
        var lamp = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Assets/Prefabs/Lamp.prefab"), post);
        lamp.name = "Lamp";
        lamp.transform.localPosition = new Vector3(0f, 3.7f, 0f);

        // A still camera, ahead of the wall. Its priority puts it ahead of the follow camera while this scene is loaded.
        var cameraObject = new GameObject("Title Camera", typeof(CinemachineCamera));
        cameraObject.transform.SetPositionAndRotation(new Vector3(-0.3f, 1.5f, -5.2f), Quaternion.Euler(2f, 0f, 0f));
        var titleCamera = cameraObject.GetComponent<CinemachineCamera>();
        titleCamera.Priority = 10;
        titleCamera.Lens.FieldOfView = 50f;

        EditorSceneManager.SaveScene(scene, TitlePath);
    }

    static void AddToBuildList()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(scene => scene.path == TitlePath)) return;

        // Right after Main, so the build list reads in the order the game opens.
        int main = scenes.FindIndex(scene => scene.path == MainPath);
        scenes.Insert(main + 1, new EditorBuildSettingsScene(TitlePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // --- Main ---

    static void BuildMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();
        Transform hud = roots.First(root => root.name == "HUD").transform;
        var buttonPrefab = Prefab(ButtonPath);

        // Below the fade, so the fade covers it, and below the pause menu.
        var menu = new GameObject("Title Menu", typeof(RectTransform), typeof(TitleMenu));
        menu.layer = LayerMask.NameToLayer("UI");
        menu.transform.SetParent(hud, false);
        menu.transform.SetSiblingIndex(hud.Find("Fade").GetSiblingIndex());
        Stretch((RectTransform)menu.transform);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.layer = menu.layer;
        panel.transform.SetParent(menu.transform, false);
        Stretch((RectTransform)panel.transform);

        // Down the left side, in the dark; the man and the lamp are on the right.
        MakeText("Title", panel.transform, "LAST LAMP", 120, new Vector2(160f, 230f), new Vector2(1000f, 160f));

        var titleMenu = menu.GetComponent<TitleMenu>();
        Button newGame = MakeButton(buttonPrefab, panel.transform, "New Game", "New Game", 0f, titleMenu.NewGame);
        // Off until there is a save to continue from (task 35).
        Button continueGame = MakeButton(buttonPrefab, panel.transform, "Continue", "Continue", -90f, null);
        continueGame.interactable = false;
        MakeButton(buttonPrefab, panel.transform, "Quit", "Quit", -180f, titleMenu.Quit);

        Set(titleMenu, "panel", panel);
        Set(titleMenu, "firstButton", newGame);
        panel.SetActive(false);

        var hudParts = new SerializedObject(hud.GetComponent<Hud>());
        SerializedProperty playOnly = hudParts.FindProperty("playOnly");
        playOnly.arraySize = 0;
        foreach (string name in PlayOnly)
        {
            Transform part = hud.Find(name);
            if (part == null)
            {
                Debug.LogError($"Build Title Screen: the HUD has no {name}.");
                continue;
            }
            playOnly.InsertArrayElementAtIndex(playOnly.arraySize);
            playOnly.GetArrayElementAtIndex(playOnly.arraySize - 1).objectReferenceValue = part.gameObject;
        }
        hudParts.ApplyModifiedPropertiesWithoutUndo();

        var loader = roots.Select(root => root.GetComponent<LevelLoader>()).First(found => found != null);
        Set(loader, "titleMenu", titleMenu);
        var loaderFields = new SerializedObject(loader);
        loaderFields.FindProperty("titleScene").stringValue = "Title";
        loaderFields.ApplyModifiedPropertiesWithoutUndo();

        // The pause menu's Quit now goes back here, so it says so.
        var pauseQuit = hud.GetComponentInChildren<PauseMenu>(true).GetComponentsInChildren<Button>(true).First(button => button.name == "Quit");
        Text pauseQuitLabel = pauseQuit.GetComponentInChildren<Text>(true);
        pauseQuitLabel.text = "Quit to title";
        PrefabUtility.RecordPrefabInstancePropertyModifications(pauseQuitLabel);

        // Cut, don't sweep, between the title camera and the follow camera: the switch happens behind the fade.
        var brain = roots.SelectMany(root => root.GetComponentsInChildren<CinemachineBrain>(true)).First();
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        EditorUtility.SetDirty(brain);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // A copy of the shared prefab: only its name, place and text change, and what it does.
    static Button MakeButton(GameObject prefab, Transform parent, string name, string text, float y, UnityAction onClick)
    {
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        copy.name = name;
        var rect = (RectTransform)copy.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(160f, y);
        copy.GetComponentInChildren<Text>().text = text;

        var button = copy.GetComponent<Button>();
        if (onClick != null) UnityEventTools.AddPersistentListener(button.onClick, onClick);
        return button;
    }

    static Text MakeText(string name, Transform parent, string text, int size, Vector2 position, Vector2 boxSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = boxSize;

        var label = go.GetComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = TextColour;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static Material Material(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);
    static GameObject Prefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    static void Set(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
