using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: makes Human_Design as a copy of Corridor_01 that keeps only its camera, light and post-processing,
/// so a human design is judged in the game's own look. Adds a stage, height guides and a blockout figure to work from.
/// Everything it makes is a normal object that can be edited by hand. Delete once the scene is confirmed.
/// </summary>
public static class HumanDesignSceneBuilder
{
    const string SourcePath = "Assets/Scenes/Corridor_01.unity";
    const string ScenePath = "Assets/Scenes/Human_Design.unity";
    const string BodyMaterialPath = "Assets/Materials/Human_Body.mat";
    const string WallMaterialPath = "Assets/Materials/Graybox_Wall.mat";
    const string FloorMaterialPath = "Assets/Materials/Graybox_Floor.mat";
    const string EyeMaterialPath = "Assets/Materials/Human_Eye.mat";
    const string FeatureMaterialPath = "Assets/Materials/Human_Feature.mat";
    const string HatMaterialPath = "Assets/Materials/Human_Hat.mat";
    const string OldPlayerName = "Old Player";

    static readonly Color FeatureColor = new Color(0.12f, 0.05f, 0.05f);

    // Same as PlayerController's standHeight and crouchHeight.
    const float StandHeight = 1.8f;
    const float CrouchHeight = 1.1f;

    static readonly string[] KeptObjects = { "Main Camera", "Directional Light", "Global Volume" };

    [MenuItem("Tools/Build Human Design Scene")]
    static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Build Human Design Scene: leave Play mode first.");
            return;
        }

        var wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
        var floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        if (wall == null || floor == null)
        {
            Debug.LogError("Build Human Design Scene: the graybox materials were not found.");
            return;
        }

        if (AssetDatabase.LoadMainAssetAtPath(ScenePath) != null)
        {
            Debug.LogError($"Build Human Design Scene: {ScenePath} already exists.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!AssetDatabase.CopyAsset(SourcePath, ScenePath))
        {
            Debug.LogError($"Build Human Design Scene: could not copy {SourcePath}.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!KeptObjects.Contains(root.name)) Object.DestroyImmediate(root);
        }

        BuildStage(wall, floor);
        BuildHuman(BodyMaterial());
        SetUpLights(scene);
        FrameCamera(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Build Human Design Scene: finished.");
    }

    // Copies the capsule and visor from the real Player, so the two can be compared at their true sizes.
    [MenuItem("Tools/Add Old Player To Human Design")]
    static void AddOldPlayer()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Add Old Player: leave Play mode first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (scene.GetRootGameObjects().Any(root => root.name == OldPlayerName))
        {
            Debug.LogError($"Add Old Player: {ScenePath} already has one.");
            return;
        }

        Scene source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Additive);
        GameObject copy = Object.Instantiate(Root(source, "Player").transform.Find("Body").gameObject);
        copy.name = OldPlayerName;
        SceneManager.MoveGameObjectToScene(copy, scene);
        copy.transform.SetPositionAndRotation(new Vector3(-1.1f, 0f, 0f), Quaternion.identity);
        EditorSceneManager.CloseScene(source, true);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Add Old Player: finished.");
    }

    // Adds a face and closes the gaps that the capsules' rounded ends leave at each joint.
    [MenuItem("Tools/Add Human Details")]
    static void AddDetails()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Add Human Details: leave Play mode first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform hips = Root(scene, "Human").transform.Find("Hips");
        Transform spine = hips.Find("Spine");
        Transform head = spine.Find("Neck Joint/Head Joint");
        if (head.Find("Face") != null)
        {
            Debug.LogError("Add Human Details: the figure already has a face.");
            return;
        }

        Material body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
        AddFace(head, body);

        // Runs the neck down into the chest, because the shoulders slope away from its flat base.
        Transform neck = spine.Find("Neck Joint/Neck");
        neck.localPosition = new Vector3(0f, 0.02f, 0f);
        neck.localScale = new Vector3(0.1f, 0.08f, 0.1f);

        Transform shoulders = Part(PrimitiveType.Capsule, "Shoulders", spine, new Vector3(0f, 0.42f, 0f), new Vector3(0.13f, 0.26f, 0.13f), body);
        shoulders.localRotation = Quaternion.Euler(0f, 0f, 90f);

        foreach (string side in new[] { "L", "R" })
        {
            Transform elbow = spine.Find($"Shoulder_{side}/Elbow_{side}");
            Ball($"ElbowBall_{side}", elbow, 0.095f, body);
            Ball($"WristBall_{side}", elbow.Find($"Wrist_{side}"), 0.065f, body);

            Transform knee = hips.Find($"Hip_{side}/Knee_{side}");
            Ball($"KneeBall_{side}", knee, 0.125f, body);
            Ball($"AnkleBall_{side}", knee.Find($"Ankle_{side}"), 0.09f, body);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Add Human Details: finished.");
    }

    // The hat's own origin sits at the brim, so it can be tilted from there. It hangs off the head joint to move with the head.
    [MenuItem("Tools/Add Top Hat")]
    static void AddTopHat()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Add Top Hat: leave Play mode first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform head = Root(scene, "Human").transform.Find("Hips/Spine/Neck Joint/Head Joint");
        if (head.Find("Top Hat") != null)
        {
            Debug.LogError("Add Top Hat: the figure already has one.");
            return;
        }

        Material felt = FlatMaterial(HatMaterialPath, new Color(0.06f, 0.06f, 0.07f));
        Material band = FlatMaterial(FeatureMaterialPath, FeatureColor);

        Transform hat = Joint("Top Hat", head, new Vector3(0f, 0.165f, 0f));
        Part(PrimitiveType.Cylinder, "Brim", hat, Vector3.zero, new Vector3(0.32f, 0.006f, 0.34f), felt);
        Part(PrimitiveType.Cylinder, "Crown", hat, new Vector3(0f, 0.085f, 0f), new Vector3(0.2f, 0.085f, 0.23f), felt);
        Part(PrimitiveType.Cylinder, "Band", hat, new Vector3(0f, 0.02f, 0f), new Vector3(0.205f, 0.015f, 0.235f), band);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Add Top Hat: finished.");
    }

    // Positions are relative to the middle of the head. The figure faces +Z.
    static void AddFace(Transform head, Material body)
    {
        Material eye = FlatMaterial(EyeMaterialPath, new Color(0.85f, 0.85f, 0.82f));
        Material dark = FlatMaterial(FeatureMaterialPath, FeatureColor);

        Transform face = Joint("Face", head, new Vector3(0f, 0.09f, 0f));

        foreach ((string side, float sign) in new[] { ("L", -1f), ("R", 1f) })
        {
            Part(PrimitiveType.Sphere, $"Eye_{side}", face, new Vector3(sign * 0.04f, 0.015f, 0.088f), Vector3.one * 0.042f, eye);
            Part(PrimitiveType.Sphere, $"Pupil_{side}", face, new Vector3(sign * 0.04f, 0.015f, 0.102f), Vector3.one * 0.02f, dark);
            Part(PrimitiveType.Sphere, $"Ear_{side}", face, new Vector3(sign * 0.095f, 0f, 0f), new Vector3(0.02f, 0.06f, 0.04f), body);
        }

        Part(PrimitiveType.Sphere, "Nose", face, new Vector3(0f, -0.02f, 0.105f), new Vector3(0.03f, 0.05f, 0.04f), body);

        Transform mouth = Part(PrimitiveType.Capsule, "Mouth", face, new Vector3(0f, -0.07f, 0.086f), new Vector3(0.014f, 0.028f, 0.02f), dark);
        mouth.localRotation = Quaternion.Euler(0f, 0f, 90f);
    }

    static void Ball(string name, Transform joint, float size, Material material)
    {
        Part(PrimitiveType.Sphere, name, joint, Vector3.zero, Vector3.one * size, material);
    }

    static Material BodyMaterial()
    {
        return FlatMaterial(BodyMaterialPath, new Color(0.55f, 0.5f, 0.45f));
    }

    static Material FlatMaterial(string path, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void BuildStage(Material wall, Material floor)
    {
        Transform stage = new GameObject("Stage").transform;
        Part(PrimitiveType.Cube, "Floor", stage, new Vector3(0f, -0.1f, 0f), new Vector3(10f, 0.2f, 10f), floor);

        // Stay at the player's real sizes while the figure changes shape.
        Transform guides = new GameObject("Height Guides").transform;
        guides.SetParent(stage, false);
        Post("Stand Height", guides, 1.1f, StandHeight, wall);
        Post("Crouch Height", guides, 1.3f, CrouchHeight, wall);
    }

    static void Post(string name, Transform parent, float x, float height, Material material)
    {
        Part(PrimitiveType.Cube, name, parent, new Vector3(x, height / 2f, 0f), new Vector3(0.04f, height, 0.04f), material);
    }

    // A figure 1.8 m tall facing +Z, the way the player faces. Each mesh hangs off an empty joint at the point it
    // bends from, so limbs can be posed by rotating a joint and resized without stretching whatever hangs below them.
    static void BuildHuman(Material body)
    {
        Transform human = new GameObject("Human").transform;

        Transform hips = Joint("Hips", human, new Vector3(0f, 0.95f, 0f));
        Part(PrimitiveType.Capsule, "Pelvis", hips, Vector3.zero, new Vector3(0.3f, 0.12f, 0.2f), body);

        Transform spine = Joint("Spine", hips, new Vector3(0f, 0.07f, 0f));
        Part(PrimitiveType.Capsule, "Torso", spine, new Vector3(0f, 0.24f, 0f), new Vector3(0.36f, 0.25f, 0.22f), body);

        Transform neck = Joint("Neck Joint", spine, new Vector3(0f, 0.48f, 0f));
        Part(PrimitiveType.Cylinder, "Neck", neck, new Vector3(0f, 0.05f, 0f), new Vector3(0.1f, 0.05f, 0.1f), body);

        Transform head = Joint("Head Joint", neck, new Vector3(0f, 0.09f, 0f));
        Part(PrimitiveType.Sphere, "Head", head, new Vector3(0f, 0.09f, 0f), new Vector3(0.19f, 0.24f, 0.22f), body);

        BuildSide("L", -1f, hips, spine, body);
        BuildSide("R", 1f, hips, spine, body);
    }

    static void BuildSide(string side, float sign, Transform hips, Transform spine, Material body)
    {
        Transform shoulder = Joint($"Shoulder_{side}", spine, new Vector3(sign * 0.22f, 0.42f, 0f));
        Part(PrimitiveType.Capsule, $"UpperArm_{side}", shoulder, new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.15f, 0.1f), body);

        Transform elbow = Joint($"Elbow_{side}", shoulder, new Vector3(0f, -0.3f, 0f));
        Part(PrimitiveType.Capsule, $"Forearm_{side}", elbow, new Vector3(0f, -0.13f, 0f), new Vector3(0.085f, 0.13f, 0.085f), body);

        Transform wrist = Joint($"Wrist_{side}", elbow, new Vector3(0f, -0.26f, 0f));
        Part(PrimitiveType.Capsule, $"Hand_{side}", wrist, new Vector3(0f, -0.09f, 0f), new Vector3(0.07f, 0.09f, 0.04f), body);

        Transform hip = Joint($"Hip_{side}", hips, new Vector3(sign * 0.1f, -0.03f, 0f));
        Part(PrimitiveType.Capsule, $"Thigh_{side}", hip, new Vector3(0f, -0.21f, 0f), new Vector3(0.15f, 0.21f, 0.15f), body);

        Transform knee = Joint($"Knee_{side}", hip, new Vector3(0f, -0.42f, 0f));
        Part(PrimitiveType.Capsule, $"Shin_{side}", knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.11f, 0.21f, 0.11f), body);

        Transform ankle = Joint($"Ankle_{side}", knee, new Vector3(0f, -0.42f, 0f));
        Part(PrimitiveType.Cube, $"Foot_{side}", ankle, new Vector3(0f, -0.04f, 0.06f), new Vector3(0.1f, 0.08f, 0.26f), body);
    }

    static Transform Joint(string name, Transform parent, Vector3 position)
    {
        Transform joint = new GameObject(name).transform;
        joint.SetParent(parent, false);
        joint.localPosition = position;
        return joint;
    }

    static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;

        // Nothing moves in this scene, and a figure with no colliders can later sit under the Player safely.
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part.transform;
    }

    // The key light is turned to face the figure's front, where the camera is. Ambient is near black, so a dim
    // fill from the opposite side keeps the back of the figure readable.
    static void SetUpLights(Scene scene)
    {
        Transform keyLight = Root(scene, "Directional Light").transform;
        keyLight.rotation = Quaternion.Euler(50f, 150f, 0f);
        Vector3 key = keyLight.forward;

        var fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.5f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.LookRotation(new Vector3(-key.x, key.y, -key.z));
    }

    static void FrameCamera(Scene scene)
    {
        Transform camera = Root(scene, "Main Camera").transform;
        camera.position = new Vector3(0f, 1.2f, 3.2f);
        camera.LookAt(new Vector3(0f, 0.95f, 0f));
    }

    static GameObject Root(Scene scene, string name)
    {
        return scene.GetRootGameObjects().First(root => root.name == name);
    }
}
