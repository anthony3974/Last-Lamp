using System.Linq;
using LastLamp;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool: makes the shared UI button prefab (Assets/Prefabs/UIButton.prefab), and builds the pause menu in Main's
/// HUD from three copies of it: Resume, Restart from checkpoint and Quit, over a dark panel. Also gives Main what any
/// menu needs: an Event System using the project's UI input actions, and a Graphic Raycaster on the HUD so the mouse
/// can click. Delete once checked.
/// </summary>
public static class PauseMenuBuilder
{
    const string MainPath = "Assets/Scenes/Main.unity";
    const string ButtonPath = "Assets/Prefabs/UIButton.prefab";
    const string ActionsPath = "Assets/InputSystem_Actions.inputactions";

    // The HUD's text colour and font.
    static readonly Color TextColour = new Color(0.9f, 0.9f, 0.9f, 1f);
    static Font font;

    [MenuItem("Tools/Build Pause Menu")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath) != null)
        {
            Debug.LogError($"Build Pause Menu: {ButtonPath} already exists, so this has already been run.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        if (actions == null || actions.FindAction("UI/Pause") == null)
        {
            Debug.LogError($"Build Pause Menu: {ActionsPath} has no UI/Pause action.");
            return;
        }

        GameObject buttonPrefab = MakeButtonPrefab();

        Scene scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        GameObject hud = scene.GetRootGameObjects().First(root => root.name == "HUD");
        if (hud.GetComponent<GraphicRaycaster>() == null) hud.AddComponent<GraphicRaycaster>();
        MakeEventSystem(actions);
        MakePauseMenu(hud.transform, buttonPrefab, actions);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Pause Menu: done.");
    }

    // A dark bar with white text. Pointing at it, or moving to it with the arrow keys, lights it lamp amber.
    static GameObject MakeButtonPrefab()
    {
        var root = new GameObject("UIButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(SelectOnHover));
        root.layer = LayerMask.NameToLayer("UI");
        ((RectTransform)root.transform).sizeDelta = new Vector2(480f, 70f);

        var image = root.GetComponent<Image>();
        image.color = Color.white;

        var button = root.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        var amber = new Color(0.52f, 0.37f, 0.16f, 0.95f);
        button.colors = new ColorBlock
        {
            normalColor = new Color(0.08f, 0.08f, 0.08f, 0.85f),
            highlightedColor = amber,
            selectedColor = amber,
            pressedColor = new Color(0.72f, 0.53f, 0.24f, 1f),
            disabledColor = new Color(0.08f, 0.08f, 0.08f, 0.4f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f,
        };
        // Buttons stand in a column; down from the bottom one goes back to the top.
        button.navigation = new Navigation { mode = Navigation.Mode.Vertical, wrapAround = true };

        Text label = MakeText("Text", root.transform, "Button", 30, Vector2.zero, Vector2.zero);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ButtonPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void MakeEventSystem(InputActionAsset actions)
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
        {
            Debug.LogWarning("Build Pause Menu: Main already has an Event System, so it was left as it is.");
            return;
        }

        var events = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var module = new SerializedObject(events.GetComponent<InputSystemUIInputModule>());
        module.FindProperty("m_ActionsAsset").objectReferenceValue = actions;
        SetAction(module, "m_PointAction", "Point");
        SetAction(module, "m_MoveAction", "Navigate");
        SetAction(module, "m_SubmitAction", "Submit");
        SetAction(module, "m_CancelAction", "Cancel");
        SetAction(module, "m_LeftClickAction", "Click");
        SetAction(module, "m_MiddleClickAction", "MiddleClick");
        SetAction(module, "m_RightClickAction", "RightClick");
        SetAction(module, "m_ScrollWheelAction", "ScrollWheel");
        SetAction(module, "m_TrackedDevicePositionAction", "TrackedDevicePosition");
        SetAction(module, "m_TrackedDeviceOrientationAction", "TrackedDeviceOrientation");
        // Clicking beside the buttons keeps the keyboard's place in the menu.
        module.FindProperty("m_DeselectOnBackgroundClick").boolValue = false;
        module.ApplyModifiedPropertiesWithoutUndo();
    }

    // The action's reference is a sub-asset of the input actions file, so the scene keeps the link.
    static void SetAction(SerializedObject module, string property, string action)
    {
        InputActionReference reference = AssetDatabase.LoadAllAssetsAtPath(ActionsPath)
            .OfType<InputActionReference>()
            .Where(candidate => !candidate.hideFlags.HasFlag(HideFlags.HideInHierarchy))
            .First(candidate => candidate.action.actionMap.name == "UI" && candidate.action.name == action);
        module.FindProperty(property).objectReferenceValue = reference;
    }

    static void MakePauseMenu(Transform hud, GameObject buttonPrefab, InputActionAsset actions)
    {
        // Last in the HUD, so it draws over everything, the fade included.
        var menu = new GameObject("Pause Menu", typeof(RectTransform), typeof(PauseMenu));
        menu.layer = LayerMask.NameToLayer("UI");
        menu.transform.SetParent(hud, false);
        menu.transform.SetAsLastSibling();
        Stretch((RectTransform)menu.transform);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.layer = menu.layer;
        panel.transform.SetParent(menu.transform, false);
        Stretch((RectTransform)panel.transform);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        MakeText("Title", panel.transform, "PAUSED", 64, new Vector2(0f, 170f), new Vector2(800f, 100f));

        // Below the middle of the screen, where the freed cursor appears, so it doesn't land on a button and pick it.
        var pauseMenu = menu.GetComponent<PauseMenu>();
        Button resume = MakeButton(buttonPrefab, panel.transform, "Resume", "Resume", -60f, pauseMenu.Resume);
        MakeButton(buttonPrefab, panel.transform, "Restart", "Restart from checkpoint", -150f, pauseMenu.RestartFromCheckpoint);
        MakeButton(buttonPrefab, panel.transform, "Quit", "Quit", -240f, pauseMenu.Quit);

        var player = Object.FindAnyObjectByType<PlayerController>();
        var fields = new SerializedObject(pauseMenu);
        fields.FindProperty("actions").objectReferenceValue = actions;
        fields.FindProperty("player").objectReferenceValue = player;
        fields.FindProperty("lives").objectReferenceValue = player.GetComponent<PlayerLives>();
        fields.FindProperty("panel").objectReferenceValue = panel;
        fields.FindProperty("firstButton").objectReferenceValue = resume;
        fields.ApplyModifiedPropertiesWithoutUndo();

        // Hidden until Esc.
        panel.SetActive(false);
    }

    // A copy of the shared prefab: only its name, place and text change, and what it does.
    static Button MakeButton(GameObject prefab, Transform parent, string name, string text, float y, UnityAction onClick)
    {
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        copy.name = name;
        ((RectTransform)copy.transform).anchoredPosition = new Vector2(0f, y);
        copy.GetComponentInChildren<Text>().text = text;

        var button = copy.GetComponent<Button>();
        UnityEventTools.AddPersistentListener(button.onClick, onClick);
        return button;
    }

    static Text MakeText(string name, Transform parent, string text, int size, Vector2 position, Vector2 boxSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = boxSize;

        var label = go.GetComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = TextColour;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
