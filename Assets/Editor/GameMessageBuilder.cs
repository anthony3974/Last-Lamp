using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-off: adds HUD/Message Panel with a GameMessage to both levels, styled like the corridor's tutorial panel,
/// and a "This door is blocked." MessageArea around the crates in front of Warehouse_01's back door.
/// Delete this script once the scenes are saved (it stays in git history).
/// </summary>
static class GameMessageBuilder
{
    const string PanelName = "Message Panel";
    const string MessagesName = "Messages";
    const string BlockedDoorName = "Back Door Blocked";

    [MenuItem("Tools/Add Game Messages")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // In the corridor it sits under the tutorial panel (y -110, 120 high), so both can show at once.
        BuildScene("Assets/Scenes/Corridor_01.unity", -230f, false);
        BuildScene("Assets/Scenes/Warehouse_01.unity", -110f, true);
    }

    static void BuildScene(string path, float panelY, bool blockedDoor)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        var hud = GameObject.Find("HUD");
        var player = Object.FindFirstObjectByType<PlayerController>();
        var lives = Object.FindFirstObjectByType<PlayerLives>();
        if (hud == null || player == null || lives == null)
        {
            Debug.LogError($"Add Game Messages: {path} is missing the HUD, the PlayerController or PlayerLives.");
            return;
        }

        var old = hud.transform.Find(PanelName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        Font font = hud.GetComponentInChildren<Text>(true).font;

        var panel = new GameObject(PanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.SetParent(hud.transform, false);
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0f, panelY);
        panelRect.sizeDelta = new Vector2(900f, 80f);

        var image = panel.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.6f);
        image.raycastTarget = false;

        var group = panel.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panelRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = new Vector2(-48f, -24f);

        var label = textObject.GetComponent<Text>();
        label.font = font;
        label.fontSize = 30;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.9f, 0.9f, 0.9f, 1f);
        label.raycastTarget = false;
        label.text = "";

        var message = panel.AddComponent<GameMessage>();
        var serialized = new SerializedObject(message);
        serialized.FindProperty("player").objectReferenceValue = player;
        serialized.FindProperty("lives").objectReferenceValue = lives;
        serialized.FindProperty("panel").objectReferenceValue = group;
        serialized.FindProperty("label").objectReferenceValue = label;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (blockedDoor) BuildBlockedDoorArea();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Add Game Messages: {path} done.");
    }

    // Around the crate pile in front of the back door: the crates cover X 12.7 to 17.3 and Z 45 to 49.3.
    static void BuildBlockedDoorArea()
    {
        var messages = GameObject.Find(MessagesName) ?? new GameObject(MessagesName);
        var old = messages.transform.Find(BlockedDoorName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var area = new GameObject(BlockedDoorName);
        area.transform.SetParent(messages.transform, false);
        area.transform.position = new Vector3(15f, 0f, 47.5f);

        var component = area.AddComponent<MessageArea>();
        var serialized = new SerializedObject(component);
        serialized.FindProperty("text").stringValue = "This door is blocked.";
        serialized.FindProperty("areaSize").vector2Value = new Vector2(9f, 8f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
