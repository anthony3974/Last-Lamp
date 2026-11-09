using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool for PLAN.md task 8: adds player noise, the noise debug view, and kickable cans to Corridor_01.
/// Everything it makes is a normal object that can be edited by hand.
/// Delete once noise is confirmed.
/// </summary>
public static class NoiseBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string CanPrefabPath = "Assets/Prefabs/Can.prefab";
    const string CanMaterialPath = "Assets/Materials/Graybox_Wall.mat";
    const string FootstepPath = "Assets/Audio/Footstep.wav";
    const string LandingPath = "Assets/Audio/Landing.wav";
    const string ClankPath = "Assets/Audio/Clank.wav";
    const float CanHeight = 0.13f;

    static readonly Vector3[] CanPositions =
    {
        new Vector3(0.8f, CanHeight, 10f),
        new Vector3(-0.7f, CanHeight, 20f),
        new Vector3(0.3f, CanHeight, 30f),
        new Vector3(-0.9f, CanHeight, 40f),
        new Vector3(0.6f, CanHeight, 50f),
        new Vector3(-3f, CanHeight, -11f),
        new Vector3(3f, CanHeight, 71f),
    };

    [MenuItem("Tools/Build Noise")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Build Noise: open {ScenePath} first.");
            return;
        }

        var player = GameObject.Find("Player");
        if (player == null || player.GetComponent<PlayerNoise>() != null)
        {
            Debug.LogError("Build Noise: the Player is missing or already has PlayerNoise.");
            return;
        }

        AddPlayerNoise(player);
        new GameObject("Noise Debug").AddComponent<NoiseDebugView>();
        PlaceCans(SaveCanPrefab());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Noise: finished.");
    }

    static void AddPlayerNoise(GameObject player)
    {
        var noise = player.AddComponent<PlayerNoise>();

        var source = player.GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        var serialized = new SerializedObject(noise);
        serialized.FindProperty("footstep").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(FootstepPath);
        serialized.FindProperty("landing").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(LandingPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static GameObject SaveCanPrefab()
    {
        var can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        can.name = "Can";
        can.transform.localScale = new Vector3(0.15f, 0.125f, 0.15f);
        can.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CanMaterialPath);

        var knockable = can.AddComponent<KnockableObject>();

        var body = can.GetComponent<Rigidbody>();
        body.mass = 0.3f;
        body.linearDamping = 0.5f;
        body.angularDamping = 0.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var source = can.GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.maxDistance = 30f;

        var serialized = new SerializedObject(knockable);
        serialized.FindProperty("clank").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ClankPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(can, CanPrefabPath);
        Object.DestroyImmediate(can);
        return prefab;
    }

    static void PlaceCans(GameObject prefab)
    {
        var root = new GameObject("Cans").transform;
        for (int i = 0; i < CanPositions.Length; i++)
        {
            var can = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            can.name = $"Can_{i + 1:00}";
            can.transform.SetParent(root);
            can.transform.position = CanPositions[i];
        }
    }
}
