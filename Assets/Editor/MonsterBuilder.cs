using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool for PLAN.md task 9: bakes the NavMesh, makes lamp pools impassable to enemies, and builds the
/// first monster and its route. Everything it makes is a normal object that can be edited by hand.
/// Delete once the monster is confirmed.
/// </summary>
public static class MonsterBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string NavMeshFolder = "Assets/Scenes/Corridor_01";
    const string LampPrefabPath = "Assets/Prefabs/Lamp.prefab";
    const string CanPrefabPath = "Assets/Prefabs/Can.prefab";
    const float WidenedWallX = 3.1f;
    const float LampHeight = 3.7f;

    static readonly Vector3[] RoutePoints =
    {
        new Vector3(0f, 0f, 10f),
        new Vector3(0f, 0f, 20f),
        new Vector3(0f, 0f, 30f),
        new Vector3(-1f, 0f, 40f),
        new Vector3(0f, 0f, 50f),
    };

    [MenuItem("Tools/Build Monster")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Build Monster: open {ScenePath} first.");
            return;
        }

        var wall = GameObject.Find("Corridor/Wall_Right");
        if (wall == null || !Mathf.Approximately(wall.transform.position.x, WidenedWallX))
        {
            Debug.LogError("Build Monster: run Tools > Widen Corridor first.");
            return;
        }

        var player = GameObject.Find("Player");
        if (player == null || GameObject.Find("Monster") != null)
        {
            Debug.LogError("Build Monster: the Player is missing or the Monster already exists.");
            return;
        }

        AddObstacleToLampPrefab();
        KeepCansOutOfNavMesh();
        BakeNavMesh();
        BuildMonster(player, BuildRoute());

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Monster: finished.");
    }

    static void AddObstacleToLampPrefab()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(LampPrefabPath);
        var obstacle = contents.GetComponent<NavMeshObstacle>();
        if (obstacle == null) obstacle = contents.AddComponent<NavMeshObstacle>();

        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = 2.6f;
        obstacle.height = LampHeight + 1f;
        obstacle.center = new Vector3(0f, -LampHeight / 2f, 0f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.carvingTimeToStationary = 0f;

        PrefabUtility.SaveAsPrefabAsset(contents, LampPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void KeepCansOutOfNavMesh()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(CanPrefabPath);
        var modifier = contents.GetComponent<NavMeshModifier>();
        if (modifier == null) modifier = contents.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;

        PrefabUtility.SaveAsPrefabAsset(contents, CanPrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);
    }

    static void BakeNavMesh()
    {
        var surface = new GameObject("Nav Surface").AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = LayerMask.GetMask("Default", "VisionBlocker");
        surface.BuildNavMesh();

        if (!AssetDatabase.IsValidFolder(NavMeshFolder)) AssetDatabase.CreateFolder("Assets/Scenes", "Corridor_01");
        AssetDatabase.CreateAsset(surface.navMeshData, $"{NavMeshFolder}/NavMesh.asset");
        EditorUtility.SetDirty(surface);
    }

    static Transform[] BuildRoute()
    {
        var root = new GameObject("Patrol Route").transform;
        var points = new Transform[RoutePoints.Length];

        for (int i = 0; i < RoutePoints.Length; i++)
        {
            points[i] = new GameObject($"Point_{i + 1:00}").transform;
            points[i].SetParent(root);
            points[i].position = RoutePoints[i];
        }
        return points;
    }

    static void BuildMonster(GameObject player, Transform[] route)
    {
        var body = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        body.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.09f));
        body.SetFloat("_Smoothness", 0.3f);
        AssetDatabase.CreateAsset(body, "Assets/Materials/Monster_Body.mat");

        var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        AssetDatabase.CreateAsset(glow, "Assets/Materials/Monster_Alert.mat");

        var monster = new GameObject("Monster");
        monster.transform.position = route[0].position;

        var collider = monster.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1.05f, 0f);
        collider.height = 2.1f;
        collider.radius = 0.4f;

        var agent = monster.AddComponent<NavMeshAgent>();
        agent.radius = 0.45f;
        agent.height = 2.1f;
        agent.stoppingDistance = 0.6f;
        agent.angularSpeed = 400f;
        agent.acceleration = 14f;

        var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        mesh.name = "Mesh";
        Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.transform.SetParent(monster.transform, false);
        mesh.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        mesh.transform.localScale = new Vector3(0.8f, 1.05f, 0.8f);
        mesh.GetComponent<MeshRenderer>().sharedMaterial = body;

        var light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        light.name = "Alert Light";
        Object.DestroyImmediate(light.GetComponent<Collider>());
        light.transform.SetParent(monster.transform, false);
        light.transform.localPosition = new Vector3(0f, 2.35f, 0f);
        light.transform.localScale = Vector3.one * 0.22f;
        var lightRenderer = light.GetComponent<MeshRenderer>();
        lightRenderer.sharedMaterial = glow;
        lightRenderer.shadowCastingMode = ShadowCastingMode.Off;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        foreach (var child in monster.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = enemyLayer;

        var enemy = monster.AddComponent<Enemy>();

        var voice = monster.GetComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.minDistance = 3f;
        voice.maxDistance = 40f;

        var serialized = new SerializedObject(enemy);
        serialized.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerController>();
        serialized.FindProperty("visibility").objectReferenceValue = player.GetComponent<PlayerVisibility>();
        serialized.FindProperty("alertLight").objectReferenceValue = lightRenderer;
        serialized.FindProperty("suspiciousSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster_Suspicious.wav");
        serialized.FindProperty("spottedSound").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Monster_Spotted.wav");
        serialized.FindProperty("visionBlockers").intValue = LayerMask.GetMask("VisionBlocker");

        var routeProperty = serialized.FindProperty("patrolRoute");
        routeProperty.arraySize = route.Length;
        for (int i = 0; i < route.Length; i++)
            routeProperty.GetArrayElementAtIndex(i).objectReferenceValue = route[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
