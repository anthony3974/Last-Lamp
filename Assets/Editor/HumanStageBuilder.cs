using System.Linq;
using LastLamp;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: sets up the imported character from Art/Human/game.py and builds a stage to judge him on.
/// The Volume, flashlight, camera and capsule are copied out of Corridor_01, so the stage matches the game.
/// Delete once the character is in the game.
/// </summary>
public static class HumanStageBuilder
{
    const string Folder = "Assets/Art/Human";
    const string ModelPath = Folder + "/Human.fbx";
    const string WeavePath = Folder + "/Human_Weave_Normal.png";
    const string ScenePath = "Assets/Scenes/Human_Stage.unity";
    const string CorridorPath = "Assets/Scenes/Corridor_01.unity";
    const string LampPath = "Assets/Prefabs/Lamp.prefab";
    const string FloorPath = "Assets/Materials/Graybox_Floor.mat";
    const string WallPath = "Assets/Materials/Graybox_Wall.mat";

    // The coat atlas is 2.34 m wide and one tile of the weave is meant to cover about 0.1 m.
    const float WeaveTiling = 23f;
    const float WeaveStrength = 0.5f;

    static readonly (string name, int size, bool weave)[] Atlases =
    {
        ("Coat", 4096, true),
        ("Clothes", 2048, false),
        ("Head", 2048, false),
        ("Hair", 1024, false),
    };

    [MenuItem("Tools/Build Human Stage")]
    public static void Build()
    {
        ImportTexture(WeavePath, 512, normal: true, srgb: false);
        foreach (var atlas in Atlases)
        {
            ImportTexture($"{Folder}/Human_{atlas.name}_Albedo.png", atlas.size, normal: false, srgb: true);
            ImportTexture($"{Folder}/Human_{atlas.name}_Normal.png", atlas.size, normal: true, srgb: false);
            ImportTexture($"{Folder}/Human_{atlas.name}_MetallicSmoothness.png", atlas.size, normal: false, srgb: false);
        }

        var materials = Atlases.ToDictionary(atlas => atlas.name, atlas => MakeMaterial(atlas.name, atlas.weave));
        ImportModel(materials.Values.ToArray());
        BuildScene();
    }

    static void ImportTexture(string path, int size, bool normal, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.maxTextureSize = size;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
    }

    static Material MakeMaterial(string atlas, bool weave)
    {
        string path = $"{Folder}/Human_{atlas}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetTexture("_BaseMap", Texture($"Human_{atlas}_Albedo"));
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", Texture($"Human_{atlas}_Normal"));
        material.SetFloat("_BumpScale", 1f);
        material.SetTexture("_MetallicGlossMap", Texture($"Human_{atlas}_MetallicSmoothness"));
        material.SetFloat("_Smoothness", 1f);
        material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_METALLICSPECGLOSSMAP");

        if (weave)
        {
            material.SetTexture("_DetailNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(WeavePath));
            material.SetFloat("_DetailNormalMapScale", WeaveStrength);
            material.SetFloat("_DetailAlbedoMapScale", 1f);
            material.SetTextureScale("_DetailAlbedoMap", new Vector2(WeaveTiling, WeaveTiling));
            material.EnableKeyword("_DETAIL_MULX2");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{name}.png");

    static void ImportModel(Material[] materials)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        foreach (var material in materials)
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
        }
        importer.SaveAndReimport();
    }

    static void BuildScene()
    {
        Scene stage = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Scene corridor = EditorSceneManager.OpenScene(CorridorPath, OpenSceneMode.Additive);

        SceneManager.SetActiveScene(corridor);
        var ambient = (RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor);
        SceneManager.SetActiveScene(stage);
        RenderSettings.ambientMode = ambient.Item1;
        RenderSettings.ambientSkyColor = ambient.Item2;
        RenderSettings.ambientEquatorColor = ambient.Item3;
        RenderSettings.ambientGroundColor = ambient.Item4;
        RenderSettings.skybox = null;

        GameObject player = FindInScene(corridor, "Player");
        Copy(FindInScene(corridor, "Global Volume"), stage);

        GameObject capsule = Copy(player.transform.Find("Body").gameObject, stage);
        capsule.name = "Capsule";
        capsule.transform.position = new Vector3(1.1f, 0f, 0f);

        GameObject flashlight = Copy(player.GetComponentInChildren<Light>(true).gameObject, stage);
        flashlight.name = "Flashlight";
        Object.DestroyImmediate(flashlight.GetComponent<PlayerFlashlight>());
        flashlight.transform.SetPositionAndRotation(new Vector3(0.25f, 1.3f, 0.4f), Quaternion.LookRotation(new Vector3(0f, -0.13f, 1f)));

        GameObject follow = Copy(FindInScene(corridor, "Main Camera"), stage);
        follow.name = "Follow Camera";
        Object.DestroyImmediate(follow.GetComponent<CinemachineBrain>());
        follow.transform.SetPositionAndRotation(new Vector3(0.5f, 1.85f, -3.2f), Quaternion.identity);

        // A view of his front in the lamp. Turn it on and Follow Camera off to look through it.
        GameObject front = Object.Instantiate(follow);
        front.name = "Front Camera";
        front.GetComponent<Camera>().fieldOfView = 30f;
        var target = new Vector3(0f, 0.98f, 0.2f);
        var position = new Vector3(-2.27f, 1.37f, 5.3f);
        front.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        front.SetActive(false);

        EditorSceneManager.CloseScene(corridor, true);

        MakeBlock("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(30f, 0.1f, 30f), FloorPath);
        MakeBlock("Wall", new Vector3(0f, 4f, -6f), new Vector3(30f, 8f, 0.2f), WallPath);

        var lamp = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LampPath), stage);
        lamp.transform.position = new Vector3(-0.55f, 4.3f, 1.75f);

        var human = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), stage);
        human.name = "Human";
        LowerArms(human.transform);

        EditorSceneManager.SaveScene(stage, ScenePath);
        Debug.Log("Build Human Stage: done.");
    }

    static GameObject Copy(GameObject source, Scene scene)
    {
        GameObject copy = Object.Instantiate(source);
        copy.name = source.name;
        copy.transform.SetParent(null);
        SceneManager.MoveGameObjectToScene(copy, scene);
        return copy;
    }

    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (found != null) return found.gameObject;
        }
        throw new System.InvalidOperationException($"Build Human Stage: no {name} in {scene.path}.");
    }

    static void MakeBlock(string name, Vector3 position, Vector3 size, string materialPath)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.position = position;
        block.transform.localScale = size;
        block.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    }

    /// <summary>The model is saved in an A-pose. These are the same arm directions as the design sheet.</summary>
    static void LowerArms(Transform human)
    {
        foreach (var (side, sign) in new[] { ("Left", -1f), ("Right", 1f) })
        {
            Transform arm = Bone(human, side + "Arm");
            Transform fore = Bone(human, side + "ForeArm");
            Transform hand = Bone(human, side + "Hand");
            Aim(arm, fore, new Vector3(sign * 0.2f, -1f, -0.02f));
            Aim(fore, hand, new Vector3(sign * 0.16f, -1f, 0.18f));
        }
    }

    static Transform Bone(Transform human, string name) =>
        human.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorig:" + name);

    static void Aim(Transform bone, Transform child, Vector3 direction)
    {
        Vector3 current = (child.position - bone.position).normalized;
        bone.rotation = Quaternion.FromToRotation(current, direction.normalized) * bone.rotation;
    }
}
