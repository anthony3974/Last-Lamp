using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool for PLAN.md task 5: adds an open yard at each end of the corridor, the night sky and brown crates.
/// Everything it makes is a normal scene object or asset that can be edited by hand.
/// Delete once the look is confirmed.
/// </summary>
public static class OpenYardBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string LampPrefabPath = "Assets/Prefabs/Lamp.prefab";
    const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";
    const string WallPath = "Assets/Materials/Graybox_Wall.mat";
    const string FloorPath = "Assets/Materials/Graybox_Floor.mat";

    const float CorridorLength = 60f;
    const float Thickness = 0.2f;
    const float YardLength = 24f;
    const float YardHalfWidth = 10f;
    const float YardWallHeight = 6f;
    const float DoorHalfWidth = 1.5f;
    const float DoorHeight = 3f;
    const float LampHeight = 3.7f;

    static Material wall;
    static Material floor;
    static Material crate;
    static int blocker;

    [MenuItem("Tools/Add Open Yards And Sky")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Add Open Yards: open {ScenePath} first.");
            return;
        }

        var endStart = GameObject.Find("End_Start");
        var endFinish = GameObject.Find("End_Finish");
        if (endStart == null || endFinish == null || GameObject.Find("Yard_Start") != null)
        {
            Debug.LogError("Add Open Yards: the corridor ends are missing or the yards already exist.");
            return;
        }

        wall = AssetDatabase.LoadAssetAtPath<Material>(WallPath);
        floor = AssetDatabase.LoadAssetAtPath<Material>(FloorPath);
        blocker = LayerMask.NameToLayer("VisionBlocker");
        crate = SaveCrateMaterial();

        Object.DestroyImmediate(endStart);
        Object.DestroyImmediate(endFinish);

        BuildYard("Yard_Start", -Thickness, -1f,
            new[] { new Vector2(-5f, -8f), new Vector2(5f, -16f) },
            new[] { new Vector2(6f, -5f), new Vector2(-6f, -13f), new Vector2(3f, -20f) });
        BuildYard("Yard_Finish", CorridorLength + Thickness, 1f,
            new[] { new Vector2(5f, 68f), new Vector2(-5f, 76f) },
            new[] { new Vector2(-6f, 65f), new Vector2(6f, 73f), new Vector2(-3f, 80f) });

        PaintCorridorCrates();
        SetUpSky();
        ExtendTestCapsule();
        LetBrownShowThrough();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Add Open Yards: finished.");
    }

    static Material SaveCrateMaterial()
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(0.42f, 0.27f, 0.14f));
        material.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(material, "Assets/Materials/Crate_Brown.mat");
        return material;
    }

    // doorPlaneZ is the yard side of the wall with the doorway. direction is +1 or -1, pointing away from the corridor.
    static void BuildYard(string name, float doorPlaneZ, float direction, Vector2[] lamps, Vector2[] crates)
    {
        var root = new GameObject(name).transform;
        float wallZ = doorPlaneZ - direction * Thickness / 2f;
        float middleZ = doorPlaneZ + direction * YardLength / 2f;
        float farZ = doorPlaneZ + direction * (YardLength + Thickness / 2f);
        float outerWidth = YardHalfWidth * 2f + Thickness * 2f;
        float wallCentreY = YardWallHeight / 2f;
        float sideX = YardHalfWidth + Thickness / 2f;

        Box("Floor", root, new Vector3(0f, -Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, YardLength), floor, 0);
        Box("Wall_Left", root, new Vector3(-sideX, wallCentreY, middleZ), new Vector3(Thickness, YardWallHeight, YardLength), wall, blocker);
        Box("Wall_Right", root, new Vector3(sideX, wallCentreY, middleZ), new Vector3(Thickness, YardWallHeight, YardLength), wall, blocker);
        Box("Wall_Far", root, new Vector3(0f, wallCentreY, farZ), new Vector3(outerWidth, YardWallHeight, Thickness), wall, blocker);

        float panelWidth = outerWidth / 2f - DoorHalfWidth;
        float panelX = DoorHalfWidth + panelWidth / 2f;
        Box("Door_Left", root, new Vector3(-panelX, wallCentreY, wallZ), new Vector3(panelWidth, YardWallHeight, Thickness), wall, blocker);
        Box("Door_Right", root, new Vector3(panelX, wallCentreY, wallZ), new Vector3(panelWidth, YardWallHeight, Thickness), wall, blocker);
        float lintelHeight = YardWallHeight - DoorHeight;
        Box("Door_Top", root, new Vector3(0f, DoorHeight + lintelHeight / 2f, wallZ), new Vector3(DoorHalfWidth * 2f, lintelHeight, Thickness), wall, blocker);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LampPrefabPath);
        for (int i = 0; i < lamps.Length; i++)
        {
            var post = new GameObject($"Lamp_Post_{i + 1:00}").transform;
            post.SetParent(root);
            post.position = new Vector3(lamps[i].x, 0f, lamps[i].y);

            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            lamp.transform.SetParent(post);
            lamp.transform.localPosition = new Vector3(0f, LampHeight, 0f);

            var pole = Box("Pole", post, post.position + new Vector3(0f, LampHeight / 2f, 0f), new Vector3(0.15f, LampHeight, 0.15f), wall, blocker);
            pole.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        var size = new Vector3(1.5f, 1.5f, 1.5f);
        for (int i = 0; i < crates.Length; i++)
            Box($"Crate_{i + 1:00}", root, new Vector3(crates[i].x, 0.75f, crates[i].y), size, crate, blocker);
    }

    static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, int layer)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    static void PaintCorridorCrates()
    {
        foreach (Transform box in GameObject.Find("Crates").transform)
            box.GetComponent<MeshRenderer>().sharedMaterial = crate;
    }

    static void SetUpSky()
    {
        var sky = new Material(Shader.Find("LastLamp/Night Cloud Sky"));
        AssetDatabase.CreateAsset(sky, "Assets/Materials/Night_Sky.mat");
        RenderSettings.skybox = sky;

        var camera = Camera.main;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.transform.position = new Vector3(0f, 2f, -21f);
        camera.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);
    }

    static void ExtendTestCapsule()
    {
        var mover = new SerializedObject(GameObject.Find("Shadow Test Capsule").GetComponent<LastLamp.ShadowTestMover>());
        mover.FindProperty("startZ").floatValue = -18f;
        mover.FindProperty("endZ").floatValue = 82f;
        mover.FindProperty("speed").floatValue = 4f;
        mover.ApplyModifiedPropertiesWithoutUndo();
    }

    // Full black and white would turn the crates gray. Everything else is gray already, so only the crates show colour.
    static void LetBrownShowThrough()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (!profile.TryGet(out ColorAdjustments color)) return;

        color.saturation.Override(-45f);
        EditorUtility.SetDirty(color);
    }
}
