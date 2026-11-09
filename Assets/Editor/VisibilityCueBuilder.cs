using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool for PLAN.md task 7: adds LampZone to the Lamp prefab, PlayerVisibility to the Player,
/// and builds the on-screen cue. Everything it makes is a normal object that can be edited by hand.
/// Delete once the cue is confirmed.
/// </summary>
public static class VisibilityCueBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string LampPrefabPath = "Assets/Prefabs/Lamp.prefab";

    [MenuItem("Tools/Build Visibility Cue")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Build Visibility Cue: open {ScenePath} first.");
            return;
        }

        var player = GameObject.Find("Player");
        if (player == null || GameObject.Find("HUD") != null)
        {
            Debug.LogError("Build Visibility Cue: the Player is missing or the HUD already exists.");
            return;
        }

        AddZoneToLampPrefab();
        var visibility = player.AddComponent<PlayerVisibility>();
        BuildHud(visibility);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Visibility Cue: finished.");
    }

    static void AddZoneToLampPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(LampPrefabPath);
        if (contents.GetComponent<LampZone>() == null) contents.AddComponent<LampZone>();
        PrefabUtility.SaveAsPrefabAsset(contents, LampPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void BuildHud(PlayerVisibility visibility)
    {
        var hud = new GameObject("HUD", typeof(RectTransform));
        hud.layer = LayerMask.NameToLayer("UI");

        var canvas = hud.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = hud.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image bar = Panel("Visibility Cue", hud.transform, new Color(0f, 0f, 0f, 0.6f));
        var barRect = bar.rectTransform;
        barRect.anchorMin = barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.anchoredPosition = new Vector2(0f, 60f);
        barRect.sizeDelta = new Vector2(360f, 16f);

        Image fill = Panel("Fill", bar.transform, Color.white);
        var fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        Image lamp = Panel("Lamp Indicator", bar.transform, Color.black);
        var lampRect = lamp.rectTransform;
        lampRect.anchorMin = lampRect.anchorMax = new Vector2(0f, 0.5f);
        lampRect.anchoredPosition = new Vector2(-24f, 0f);
        lampRect.sizeDelta = new Vector2(24f, 24f);

        var cue = bar.gameObject.AddComponent<VisibilityCue>();
        var serialized = new SerializedObject(cue);
        serialized.FindProperty("visibility").objectReferenceValue = visibility;
        serialized.FindProperty("fill").objectReferenceValue = fill;
        serialized.FindProperty("lampIndicator").objectReferenceValue = lamp;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static Image Panel(string name, Transform parent, Color color)
    {
        var panel = new GameObject(name, typeof(RectTransform));
        panel.layer = LayerMask.NameToLayer("UI");
        panel.transform.SetParent(parent, false);

        var image = panel.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }
}
