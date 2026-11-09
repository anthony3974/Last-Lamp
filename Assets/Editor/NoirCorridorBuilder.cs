using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool for PLAN.md task 5: builds the graybox corridor, lamps and noir look into Corridor_01.
/// Everything it makes is a normal scene object or asset that can be edited by hand.
/// Delete once the look is confirmed.
/// </summary>
public static class NoirCorridorBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string MaterialFolder = "Assets/Materials";
    const string LampPrefabPath = "Assets/Prefabs/Lamp.prefab";
    const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";
    const string PcPipelinePath = "Assets/Settings/PC_RPAsset.asset";

    const float Length = 60f;
    const float Width = 5f;
    const float Height = 4f;
    const float Thickness = 0.2f;
    const int LampCount = 6;
    const float LampSpacing = 10f;
    const float FirstLampZ = 5f;

    [MenuItem("Tools/Build Noir Corridor")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Build Noir Corridor: open {ScenePath} first.");
            return;
        }
        if (GameObject.Find("Corridor") != null)
        {
            Debug.LogError("Build Noir Corridor: this scene already has a Corridor.");
            return;
        }

        Material wall = SaveMaterial("Graybox_Wall", "Universal Render Pipeline/Lit", new Color(0.55f, 0.55f, 0.55f));
        Material floor = SaveMaterial("Graybox_Floor", "Universal Render Pipeline/Lit", new Color(0.4f, 0.4f, 0.4f));
        Material glow = SaveMaterial("Lamp_Glow", "Universal Render Pipeline/Unlit", new Color(6f, 6f, 6f));

        BuildCorridor(wall, floor);
        BuildLamps(glow);
        BuildTestCapsule(wall);
        SetUpSceneLighting();
        SetUpCamera();
        SetUpNoirVolume();
        RaiseShadowAtlas();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Noir Corridor: finished.");
    }

    static Material SaveMaterial(string name, string shaderName, Color color)
    {
        var material = new Material(Shader.Find(shaderName));
        material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(material, $"{MaterialFolder}/{name}.mat");
        return material;
    }

    static void BuildCorridor(Material wall, Material floor)
    {
        var root = new GameObject("Corridor").transform;
        int blocker = LayerMask.NameToLayer("VisionBlocker");
        float outerWidth = Width + Thickness * 2f;
        float middleZ = Length / 2f;

        Box("Floor", root, new Vector3(0f, -Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, Length), floor, 0);
        Box("Ceiling", root, new Vector3(0f, Height + Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, Length), wall, 0);

        float wallX = Width / 2f + Thickness / 2f;
        Box("Wall_Left", root, new Vector3(-wallX, Height / 2f, middleZ), new Vector3(Thickness, Height, Length), wall, blocker);
        Box("Wall_Right", root, new Vector3(wallX, Height / 2f, middleZ), new Vector3(Thickness, Height, Length), wall, blocker);
        Box("End_Start", root, new Vector3(0f, Height / 2f, -Thickness / 2f), new Vector3(outerWidth, Height, Thickness), wall, blocker);
        Box("End_Finish", root, new Vector3(0f, Height / 2f, Length + Thickness / 2f), new Vector3(outerWidth, Height, Thickness), wall, blocker);

        var crates = new GameObject("Crates").transform;
        crates.SetParent(root);
        var size = new Vector3(1.5f, 1.5f, 1.5f);
        Box("Crate_01", crates, new Vector3(1.2f, 0.75f, 7.5f), size, wall, blocker);
        Box("Crate_02", crates, new Vector3(-1.2f, 0.75f, 18f), size, wall, blocker);
        Box("Crate_03", crates, new Vector3(1f, 0.75f, 28f), size, wall, blocker);
        Box("Crate_04", crates, new Vector3(-1f, 0.75f, 42f), size, wall, blocker);
    }

    static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, int layer)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void BuildLamps(Material glow)
    {
        GameObject prefab = SaveLampPrefab(glow);
        var root = new GameObject("Lamps").transform;

        for (int i = 0; i < LampCount; i++)
        {
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            lamp.name = $"Lamp_{i + 1:00}";
            lamp.transform.SetParent(root);
            lamp.transform.position = new Vector3(0f, Height - 0.3f, FirstLampZ + i * LampSpacing);
        }
    }

    static GameObject SaveLampPrefab(Material glow)
    {
        var lamp = new GameObject("Lamp");

        var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixture.name = "Fixture";
        fixture.transform.SetParent(lamp.transform);
        fixture.transform.localPosition = Vector3.zero;
        fixture.transform.localScale = new Vector3(0.6f, 0.1f, 0.6f);
        Object.DestroyImmediate(fixture.GetComponent<BoxCollider>());
        var fixtureRenderer = fixture.GetComponent<MeshRenderer>();
        fixtureRenderer.sharedMaterial = glow;
        fixtureRenderer.shadowCastingMode = ShadowCastingMode.Off;

        var lightObject = new GameObject("Spot Light");
        lightObject.transform.SetParent(lamp.transform);
        lightObject.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Spot;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.color = Color.white;
        light.intensity = 25f;
        light.range = 8f;
        light.spotAngle = 80f;
        light.innerSpotAngle = 65f;
        light.shadows = LightShadows.Hard;
        light.shadowStrength = 1f;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(lamp, LampPrefabPath);
        Object.DestroyImmediate(lamp);
        return prefab;
    }

    static void BuildTestCapsule(Material material)
    {
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Shadow Test Capsule";
        capsule.transform.position = new Vector3(0f, 1f, 2f);
        capsule.GetComponent<MeshRenderer>().sharedMaterial = material;
        capsule.AddComponent<ShadowTestMover>();
    }

    static void SetUpSceneLighting()
    {
        var sun = GameObject.Find("Directional Light");
        if (sun != null) sun.SetActive(false);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.05f);
        RenderSettings.reflectionIntensity = 0f;
    }

    static void SetUpCamera()
    {
        var camera = Camera.main;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.transform.position = new Vector3(0f, 2.4f, 1f);
        camera.transform.rotation = Quaternion.Euler(8f, 0f, 0f);
    }

    static void SetUpNoirVolume()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);

        if (!profile.TryGet(out ColorAdjustments color))
        {
            color = profile.Add<ColorAdjustments>();
            color.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(color, profile);
        }
        color.active = true;
        color.saturation.Override(-100f);
        color.contrast.Override(35f);
        EditorUtility.SetDirty(color);

        if (profile.TryGet(out Vignette vignette))
        {
            vignette.active = true;
            vignette.intensity.Override(0.4f);
            vignette.smoothness.Override(0.4f);
            EditorUtility.SetDirty(vignette);
        }

        EditorUtility.SetDirty(profile);
    }

    // Six lamps at the High shadow tier need 1024 each, which does not fit in the 2048 default.
    static void RaiseShadowAtlas()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcPipelinePath);
        pipeline.additionalLightsShadowmapResolution = 4096;
        EditorUtility.SetDirty(pipeline);
    }
}
