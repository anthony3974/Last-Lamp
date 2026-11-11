using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool for PLAN.md task 24: adds the tutorial to Corridor_01. It makes a Tutorial object with one child per
/// prompt, in the order they show, and a text panel at the top of the HUD. Everything it makes is a normal object
/// that can be edited by hand. Delete once the tutorial is confirmed.
/// </summary>
public static class TutorialBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";

    // The start yard: 20.4 m wide, from the far wall at z -24 to the corridor mouth at z 0.
    static readonly Vector3 YardCentre = new Vector3(0f, 0f, -12f);
    static readonly Vector2 YardSize = new Vector2(20.4f, 24f);

    struct Prompt
    {
        public string Name;
        public string Text;
        public TutorialTrigger ShowWhen;
        public Vector3 Position;
        public Vector2 AreaSize;
        public TutorialDone HideWhen;
        public float Amount;
    }

    static Prompt InYard(string name, string text, TutorialDone hideWhen, float amount = 0f)
    {
        return new Prompt { Name = name, Text = text, ShowWhen = TutorialTrigger.Area, Position = YardCentre, AreaSize = YardSize, HideWhen = hideWhen, Amount = amount };
    }

    static Prompt When(string name, string text, TutorialTrigger showWhen, TutorialDone hideWhen, float amount = 0f)
    {
        return new Prompt { Name = name, Text = text, ShowWhen = showWhen, HideWhen = hideWhen, Amount = amount };
    }

    static readonly Prompt[] Prompts =
    {
        // Controls, in order, in the start yard where there are no monsters.
        InYard("01 Move", "{Move}  Move        Mouse  Look", TutorialDone.Walked, 3f),
        InYard("02 Sprint", "Hold {Sprint} to sprint. Sprinting is fast, but loud.", TutorialDone.Sprinted, 1f),
        InYard("03 Jump", "{Jump}  Jump. Landing makes noise too.", TutorialDone.Jumped),
        InYard("04 Crouch", "Hold {Crouch} to crouch. Crouched, you make no sound and are harder to see.", TutorialDone.CrouchWalked, 2f),
        InYard("05 Visibility", "The bar at the bottom shows how visible you are in the dark.\nMoving fills it, crouching keeps it low.", TutorialDone.Timer, 6f),
        InYard("06 Flashlight", "Your flashlight points where you look.\nHold {Flashlight} for a brighter beam, but monsters notice you sooner.", TutorialDone.BrightFlashlight, 0.5f),
        InYard("07 Fight", "{Dodge}  Dodge        {Attack}  Punch\nA punch stuns a monster, but not again straight away.", TutorialDone.DodgedAndPunched),

        // The rules, in the first stretch of the corridor.
        When("08 Light", "Light is safe. Monsters won't step into a lit pool\nunless they're already chasing you.", TutorialTrigger.InLampPool, TutorialDone.Timer, 5f),
        When("09 Glow", "Watch a monster's glow. Grey: patrolling.\nYellow or orange: it noticed something. Red: it's chasing you.", TutorialTrigger.InLampPool, TutorialDone.Timer, 6f),
        When("10 Noticed", "It noticed something. Back off, get into the light or hide before it turns red.", TutorialTrigger.MonsterNoticed, TutorialDone.Timer, 4f),
        When("11 Hide", "{Interact}  Hide. It only works if the monster doesn't see you get in.", TutorialTrigger.HidingSpotInReach, TutorialDone.Hidden),
        When("12 Checkpoint", "Checkpoint. If you're caught, you lose one of your 3 lives and come back here.", TutorialTrigger.NewCheckpoint, TutorialDone.Timer, 5f),
        When("13 Caught", "Caught: one life gone. Dodge when you see the white flash.", TutorialTrigger.Caught, TutorialDone.Timer, 4f),
        new Prompt
        {
            Name = "14 On Your Own", Text = "The rest is up to you.", ShowWhen = TutorialTrigger.Area,
            Position = new Vector3(0f, 0f, 33f), AreaSize = new Vector2(6.4f, 2f), HideWhen = TutorialDone.Timer, Amount = 3f,
        },
    };

    [MenuItem("Tools/Build Corridor Tutorial")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        var player = GameObject.Find("Player");
        var hud = GameObject.Find("HUD");
        if (scene.path != ScenePath || player == null || hud == null)
        {
            Debug.LogError($"Build Corridor Tutorial: open {ScenePath} first.");
            return;
        }

        if (GameObject.Find("Tutorial") != null)
        {
            Debug.LogError("Build Corridor Tutorial: this has already been built.");
            return;
        }

        var controller = player.GetComponent<PlayerController>();
        var flashlight = player.GetComponentInChildren<PlayerFlashlight>(true);
        if (controller == null || flashlight == null)
        {
            Debug.LogError("Build Corridor Tutorial: the Player is missing its PlayerController or PlayerFlashlight.");
            return;
        }

        (GameObject panel, Text label) = BuildPanel(hud);

        var root = new GameObject("Tutorial");
        foreach (var prompt in Prompts) AddPrompt(root.transform, prompt);

        var tutorial = root.AddComponent<Tutorial>();
        var serialized = new SerializedObject(tutorial);
        serialized.FindProperty("actions").objectReferenceValue = new SerializedObject(controller).FindProperty("actions").objectReferenceValue;
        serialized.FindProperty("player").objectReferenceValue = controller;
        serialized.FindProperty("combat").objectReferenceValue = player.GetComponent<PlayerCombat>();
        serialized.FindProperty("hiding").objectReferenceValue = player.GetComponent<PlayerHiding>();
        serialized.FindProperty("lives").objectReferenceValue = player.GetComponent<PlayerLives>();
        serialized.FindProperty("flashlight").objectReferenceValue = flashlight;
        serialized.FindProperty("panel").objectReferenceValue = panel;
        serialized.FindProperty("label").objectReferenceValue = label;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Build Corridor Tutorial: finished, with {Prompts.Length} prompts.");
    }

    static void AddPrompt(Transform parent, Prompt prompt)
    {
        var promptObject = new GameObject(prompt.Name);
        promptObject.transform.SetParent(parent, false);
        promptObject.transform.position = prompt.Position;

        var component = promptObject.AddComponent<TutorialPrompt>();
        var serialized = new SerializedObject(component);
        serialized.FindProperty("text").stringValue = prompt.Text;
        serialized.FindProperty("showWhen").enumValueIndex = (int)prompt.ShowWhen;
        if (prompt.ShowWhen == TutorialTrigger.Area) serialized.FindProperty("areaSize").vector2Value = prompt.AreaSize;
        serialized.FindProperty("hideWhen").enumValueIndex = (int)prompt.HideWhen;
        serialized.FindProperty("amount").floatValue = prompt.Amount;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // A dark strip at the top centre, clear of the lives at the top left and the cues at the bottom.
    static (GameObject, Text) BuildPanel(GameObject hud)
    {
        int uiLayer = LayerMask.NameToLayer("UI");

        var panel = new GameObject("Tutorial Panel", typeof(RectTransform));
        panel.layer = uiLayer;
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(hud.transform, false);
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -110f);
        panelRect.sizeDelta = new Vector2(1200f, 120f);

        var background = panel.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.6f);
        background.raycastTarget = false;

        var labelObject = new GameObject("Text", typeof(RectTransform));
        labelObject.layer = uiLayer;
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.SetParent(panelRect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(24f, 12f);
        labelRect.offsetMax = new Vector2(-24f, -12f);

        var label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 30;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.9f, 0.9f, 0.9f);
        label.raycastTarget = false;

        panel.SetActive(false);
        return (panel, label);
    }
}
