using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool for PLAN.md task 11: makes the checkpoint prefab, places two, gives the Player lives and a punch,
/// connects the Monster and adds the HUD text. Everything it makes is a normal object that can be edited by hand.
/// Delete once getting caught is confirmed.
/// </summary>
public static class CaughtBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string PrefabPath = "Assets/Prefabs/Checkpoint.prefab";
    const string MarkerMaterialPath = "Assets/Materials/Lamp_Glow.mat";
    const float CheckpointHeight = 0.05f;

    [MenuItem("Tools/Build Getting Caught")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        var player = GameObject.Find("Player");
        var monster = GameObject.Find("Monster");
        var hud = GameObject.Find("HUD");
        if (scene.path != ScenePath || player == null || monster == null || hud == null)
        {
            Debug.LogError($"Build Getting Caught: open {ScenePath} first.");
            return;
        }

        if (player.GetComponent<PlayerLives>() != null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.LogError("Build Getting Caught: this has already been built.");
            return;
        }

        var markerMaterial = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
        if (markerMaterial == null)
        {
            Debug.LogError($"Build Getting Caught: {MarkerMaterialPath} was not found.");
            return;
        }

        GameObject prefab = BuildPrefab(markerMaterial);
        var group = new GameObject("Checkpoints").transform;
        Checkpoint first = Place(prefab, group, "Checkpoint_01", new Vector3(0f, CheckpointHeight, -20f));
        Place(prefab, group, "Checkpoint_02", new Vector3(2f, CheckpointHeight, 25f));

        player.AddComponent<PlayerCombat>();
        var lives = player.AddComponent<PlayerLives>();
        SetReference(lives, "firstCheckpoint", first);
        SetReference(monster.GetComponent<Enemy>(), "lives", lives);

        BuildHud(hud, lives);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Getting Caught: finished.");
    }

    static GameObject BuildPrefab(Material markerMaterial)
    {
        var root = new GameObject("Checkpoint");
        root.AddComponent<Checkpoint>();

        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Marker";
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(root.transform, false);
        marker.transform.localPosition = Vector3.up * -0.03f;
        marker.transform.localScale = new Vector3(1.5f, 0.01f, 1.5f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = markerMaterial;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Checkpoint Place(GameObject prefab, Transform group, string name, Vector3 position)
    {
        var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        placed.name = name;
        placed.transform.position = position;
        return placed.GetComponent<Checkpoint>();
    }

    static void BuildHud(GameObject hud, PlayerLives lives)
    {
        Text livesLabel = AddLabel(hud, "Lives", new Vector2(0f, 1f), new Vector2(130f, -40f), new Vector2(220f, 40f), 26, TextAnchor.MiddleLeft);
        Text caughtLabel = AddLabel(hud, "Caught Message", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(800f, 90f), 64, TextAnchor.MiddleCenter);
        caughtLabel.enabled = false;

        var cue = livesLabel.gameObject.AddComponent<LivesCue>();
        var serialized = new SerializedObject(cue);
        serialized.FindProperty("lives").objectReferenceValue = lives;
        serialized.FindProperty("livesLabel").objectReferenceValue = livesLabel;
        serialized.FindProperty("caughtLabel").objectReferenceValue = caughtLabel;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static Text AddLabel(GameObject hud, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
    {
        var labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.layer = LayerMask.NameToLayer("UI");

        var rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(hud.transform, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var label = labelObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = new Color(0.85f, 0.85f, 0.85f);
        label.raycastTarget = false;
        return label;
    }

    static void SetReference(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
