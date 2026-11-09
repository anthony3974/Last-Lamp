using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool for PLAN.md task 10: makes the hiding spot prefab, places one in the corridor and adds the
/// on-screen prompt. Everything it makes is a normal object that can be edited by hand.
/// Delete once the hiding spot is confirmed.
/// </summary>
public static class HidingSpotBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string PrefabPath = "Assets/Prefabs/HidingSpot.prefab";
    const string BodyMaterialPath = "Assets/Materials/Crate_Brown.mat";
    const string DoorMaterialPath = "Assets/Materials/Graybox_Wall.mat";

    static readonly Vector3 Size = new Vector3(1.4f, 2.2f, 0.8f);

    [MenuItem("Tools/Build Hiding Spot")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        var player = GameObject.Find("Player");
        var hud = GameObject.Find("HUD");
        if (scene.path != ScenePath || player == null || hud == null)
        {
            Debug.LogError($"Build Hiding Spot: open {ScenePath} first.");
            return;
        }

        if (player.GetComponent<PlayerHiding>() != null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.LogError("Build Hiding Spot: the hiding spot has already been built.");
            return;
        }

        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
        var doorMaterial = AssetDatabase.LoadAssetAtPath<Material>(DoorMaterialPath);
        if (bodyMaterial == null || doorMaterial == null)
        {
            Debug.LogError("Build Hiding Spot: a graybox material was not found.");
            return;
        }

        GameObject prefab = BuildPrefab(bodyMaterial, doorMaterial);
        PlaceInCorridor(prefab);
        var hiding = player.AddComponent<PlayerHiding>();
        BuildPrompt(hud, hiding);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Hiding Spot: finished.");
    }

    static GameObject BuildPrefab(Material bodyMaterial, Material doorMaterial)
    {
        var root = new GameObject("HidingSpot");

        var collider = root.AddComponent<BoxCollider>();
        collider.center = Vector3.up * (Size.y / 2f);
        collider.size = Size;

        // Baked NavMesh doesn't know about this object, so it carves its own hole like a lamp does.
        var obstacle = root.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = collider.center;
        obstacle.size = Size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.carvingTimeToStationary = 0f;

        AddCube(root, "Body", collider.center, Size, bodyMaterial);
        AddCube(root, "Door", new Vector3(0f, 1f, Size.z / 2f + 0.01f), new Vector3(1.1f, 1.8f, 0.02f), doorMaterial);

        var exit = new GameObject("Exit Point").transform;
        exit.SetParent(root.transform, false);
        exit.localPosition = new Vector3(0f, 0.05f, 1.1f);

        var spot = root.AddComponent<HidingSpot>();
        var serialized = new SerializedObject(spot);
        serialized.FindProperty("exitPoint").objectReferenceValue = exit;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        int blockerLayer = LayerMask.NameToLayer("VisionBlocker");
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = blockerLayer;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void AddCube(GameObject parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(parent.transform, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Against the right wall in the dark gap between the Lamp_02 and Lamp_03 pools, with its door facing the corridor.
    static void PlaceInCorridor(GameObject prefab)
    {
        var group = new GameObject("Hiding Spots").transform;
        var spot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        spot.name = "Hiding_Spot_01";
        spot.transform.SetPositionAndRotation(new Vector3(2.6f, 0f, 20f), Quaternion.Euler(0f, -90f, 0f));
    }

    static void BuildPrompt(GameObject hud, PlayerHiding hiding)
    {
        var prompt = new GameObject("Interact Prompt", typeof(RectTransform));
        prompt.layer = LayerMask.NameToLayer("UI");

        var rect = prompt.GetComponent<RectTransform>();
        rect.SetParent(hud.transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 110f);
        rect.sizeDelta = new Vector2(400f, 40f);

        var label = prompt.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 26;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.85f, 0.85f, 0.85f);
        label.raycastTarget = false;
        label.enabled = false;

        var serialized = new SerializedObject(prompt.AddComponent<InteractPrompt>());
        serialized.FindProperty("hiding").objectReferenceValue = hiding;
        serialized.FindProperty("label").objectReferenceValue = label;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
