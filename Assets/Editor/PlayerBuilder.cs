using LastLamp;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool for PLAN.md task 6: builds the player and follow camera into Corridor_01 and removes the
/// shadow test capsule. Everything it makes is a normal scene object that can be edited by hand.
/// Delete once the player is confirmed.
/// </summary>
public static class PlayerBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
    const string BodyMaterialPath = "Assets/Materials/Graybox_Wall.mat";
    const string VisorMaterialPath = "Assets/Materials/Graybox_Floor.mat";
    const string TestMoverPath = "Assets/Scripts/Player/ShadowTestMover.cs";

    [MenuItem("Tools/Build Player")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Build Player: open {ScenePath} first.");
            return;
        }
        if (GameObject.Find("Player") != null)
        {
            Debug.LogError("Build Player: this scene already has a Player.");
            return;
        }

        int worldMask = LayerMask.GetMask("Default", "VisionBlocker");
        Transform cameraTarget = BuildPlayer(worldMask);
        BuildCamera(cameraTarget, worldMask);

        var capsule = GameObject.Find("Shadow Test Capsule");
        if (capsule != null) Object.DestroyImmediate(capsule);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.DeleteAsset(TestMoverPath);
        Debug.Log("Build Player: finished.");
    }

    static Transform BuildPlayer(int worldMask)
    {
        var root = new GameObject("Player");
        root.transform.position = new Vector3(0f, 0.05f, -20f);

        var character = root.AddComponent<CharacterController>();
        character.height = 1.8f;
        character.radius = 0.35f;
        character.center = new Vector3(0f, 0.9f, 0f);
        character.stepOffset = 0.3f;

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);

        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
        var visorMaterial = AssetDatabase.LoadAssetAtPath<Material>(VisorMaterialPath);
        Part(PrimitiveType.Capsule, "Mesh", body, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), bodyMaterial);
        Part(PrimitiveType.Cube, "Visor", body, new Vector3(0f, 1.5f, 0.3f), new Vector3(0.35f, 0.12f, 0.15f), visorMaterial);

        var cameraTarget = new GameObject("Camera Target").transform;
        cameraTarget.SetParent(root.transform, false);
        cameraTarget.localPosition = new Vector3(0f, 1.55f, 0f);

        var player = root.AddComponent<PlayerController>();
        var serialized = new SerializedObject(player);
        serialized.FindProperty("actions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
        serialized.FindProperty("cameraTarget").objectReferenceValue = cameraTarget;
        serialized.FindProperty("body").objectReferenceValue = body;
        serialized.FindProperty("obstacleMask").intValue = worldMask;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        int playerLayer = LayerMask.NameToLayer("Player");
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = playerLayer;

        return cameraTarget;
    }

    static void Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void BuildCamera(Transform cameraTarget, int worldMask)
    {
        var mainCamera = Camera.main;
        if (mainCamera.GetComponent<CinemachineBrain>() == null)
            mainCamera.gameObject.AddComponent<CinemachineBrain>();

        var cameraObject = new GameObject("Player Follow Camera");
        var virtualCamera = cameraObject.AddComponent<CinemachineCamera>();
        virtualCamera.Target.TrackingTarget = cameraTarget;
        virtualCamera.Lens.FieldOfView = 60f;

        var follow = cameraObject.AddComponent<CinemachineThirdPersonFollow>();
        follow.ShoulderOffset = new Vector3(0.5f, 0f, 0f);
        follow.VerticalArmLength = 0.3f;
        follow.CameraSide = 1f;
        follow.CameraDistance = 3.2f;

        var obstacles = follow.AvoidObstacles;
        obstacles.Enabled = true;
        obstacles.CollisionFilter = worldMask;
        obstacles.CameraRadius = 0.2f;
        follow.AvoidObstacles = obstacles;
    }
}
