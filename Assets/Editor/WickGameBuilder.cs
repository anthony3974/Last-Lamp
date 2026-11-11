using System.Collections.Generic;
using System.Linq;
using LastLamp;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: imports the Wick's Mixamo clips, builds its Animator and the Wick prefab, and puts it on every monster in
/// both levels in place of the capsule and the sphere. Widens the enemies' NavMesh agent to fit its arms and rebakes both
/// levels. Safe to run again. Delete once the Wick is confirmed in the game, and keep it in git history.
/// </summary>
public static class WickGameBuilder
{
    const string Folder = "Assets/Art/Wick";
    const string ClipFolder = Folder + "/Animations";
    const string ModelPath = Folder + "/Wick.fbx";
    const string ControllerPath = Folder + "/Wick.controller";
    const string HeadMaskPath = Folder + "/Wick_Head.mask";
    const string PrefabPath = "Assets/Prefabs/Wick.prefab";
    const string LampPath = "Assets/Prefabs/Lamp.prefab";
    const string NavMeshSettingsPath = "ProjectSettings/NavMeshAreas.asset";
    const string Prefix = "mixamorig:";
    const string LitArea = "Lit";

    static readonly string[] Scenes = { "Assets/Scenes/Corridor_01.unity", "Assets/Scenes/Warehouse_01.unity" };
    static readonly string[] OldParts = { "Mesh", "Alert Light", "Wick" };

    // The Wick's arms hang about 0.53 m out from its middle. LampZone widens the Lit areas by the same amount.
    const float AgentRadius = 0.55f;

    // How fast each clip's feet travel on the Wick, measured in Blender at its leg length (hips 1.06 m high).
    const float WalkClipSpeed = 1.23f;
    const float RunClipSpeed = 2.31f;
    // Enemy's default speeds: patrol, investigate and chase. Search, at 2.2, blends between the walk and the run.
    const float PatrolSpeed = 1.8f;
    const float InvestigateSpeed = 2.8f;
    const float ChaseSpeed = 4.8f;

    const float ClipFrameRate = 30f;
    const float QuickBlend = 0.1f;
    const float Blend = 0.25f;

    class Clip
    {
        public string Name;
        public bool Loop;
        public int First = -1;
        public int Last = -1;
    }

    static readonly Clip[] Clips =
    {
        new Clip { Name = "Idle", Loop = true },
        new Clip { Name = "Walk", Loop = true },
        new Clip { Name = "Run", Loop = true },
        // From the wind-up to the swipe landing, so the swipe lands as the 0.6 s grab ends.
        new Clip { Name = "Grab", First = 18, Last = 40 },
        new Clip { Name = "Stunned" },
        new Clip { Name = "LookAround", Loop = true },
    };

    [MenuItem("Tools/Put Wick In Game")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnforceTPose((ModelImporter)AssetImporter.GetAtPath(ModelPath));
        foreach (Clip clip in Clips) ImportClip(clip);
        AnimatorController controller = BuildController();
        GameObject prefab = BuildPrefab(controller);

        WidenAgent();
        ResizeLitAreas(LoadLampPrefab());
        foreach (string scene in Scenes) PlaceInScene(scene, prefab);
        Debug.Log("Put Wick In Game: done.");
    }

    /// <summary>
    /// The model is saved hunched, with its arms hanging and its knees bent. The Avatar is told where a T-pose would put
    /// each of them, with the spine straight up, so a clip's posture replaces the modelled hunch instead of adding to it.
    /// The neck and head keep their modelled angle to the chest, so the head still thrusts forward.
    /// </summary>
    static void EnforceTPose(ModelImporter importer)
    {
        GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        Transform root = model.transform;

        Aim(Bone(root, "Hips"), Bone(root, "Spine"), Vector3.up);
        Aim(Bone(root, "Spine"), Bone(root, "Spine1"), Vector3.up);
        Aim(Bone(root, "Spine1"), Bone(root, "Spine2"), Vector3.up);
        Aim(Bone(root, "Spine2"), Bone(root, "Neck"), Vector3.up);

        foreach (var (side, sign) in new[] { ("Left", -1f), ("Right", 1f) })
        {
            var along = new Vector3(sign, 0f, 0f);
            Aim(Bone(root, side + "Arm"), Bone(root, side + "ForeArm"), along);
            Aim(Bone(root, side + "ForeArm"), Bone(root, side + "Hand"), along);
            Aim(Bone(root, side + "Hand"), Bone(root, side + "HandMiddle1"), along);
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
            {
                Vector3 direction = finger == "Thumb" ? new Vector3(sign, 0f, 1f) : along;
                Aim(Bone(root, $"{side}Hand{finger}1"), Bone(root, $"{side}Hand{finger}2"), direction);
                Aim(Bone(root, $"{side}Hand{finger}2"), Bone(root, $"{side}Hand{finger}3"), direction);
            }

            // Straighten the leg, then turn the foot back to where it pointed, so it still points forward and down.
            Transform foot = Bone(root, side + "Foot");
            Transform toe = Bone(root, side + "ToeBase");
            Vector3 footDirection = toe.position - foot.position;
            Aim(Bone(root, side + "UpLeg"), Bone(root, side + "Leg"), Vector3.down);
            Aim(Bone(root, side + "Leg"), foot, Vector3.down);
            Aim(foot, toe, footDirection);
        }

        var bones = root.GetComponentsInChildren<Transform>(true).ToDictionary(bone => bone.name);
        HumanDescription description = importer.humanDescription;
        SkeletonBone[] skeleton = description.skeleton;
        for (int i = 0; i < skeleton.Length; i++)
        {
            if (bones.TryGetValue(skeleton[i].name, out Transform bone)) skeleton[i].rotation = bone.localRotation;
        }
        description.skeleton = skeleton;
        importer.humanDescription = description;
        importer.SaveAndReimport();
        Object.DestroyImmediate(model);
    }

    static string PathOf(string clip) => $"{ClipFolder}/{clip}.fbx";

    /// <summary>All travel is left out of the pose. The Animator doesn't move the Wick: its NavMeshAgent does.</summary>
    static void ImportClip(Clip clip)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(PathOf(clip.Name));
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.SaveAndReimport();

        ModelImporterClipAnimation settings = importer.defaultClipAnimations[0];
        float start = settings.firstFrame;
        settings.name = clip.Name;
        if (clip.First >= 0)
        {
            settings.firstFrame = start + clip.First;
            settings.lastFrame = start + clip.Last;
        }
        settings.loopTime = clip.Loop;
        settings.loopPose = clip.Loop;
        settings.lockRootRotation = true;
        settings.keepOriginalOrientation = true;
        settings.lockRootHeightY = true;
        settings.keepOriginalPositionY = true;
        // Taken from the centre of mass, because Mixamo's own root never moves. The mutant run travels 4 m a cycle.
        settings.lockRootPositionXZ = false;
        settings.keepOriginalPositionXZ = false;

        importer.clipAnimations = new[] { settings };
        importer.SaveAndReimport();
    }

    static AnimationClip Load(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(PathOf(name)).OfType<AnimationClip>().First(clip => clip.name == name);

    static AvatarMask BuildHeadMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(HeadMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, HeadMaskPath);
        }
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Head);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attacking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Stunned", AnimatorControllerParameterType.Bool);

        // WickAnimator lowers the hips during the walk in OnAnimatorIK, which only runs with the IK pass on.
        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        var tree = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.children = new[]
        {
            Child("Idle", 0f, 1f),
            Child("Walk", PatrolSpeed, PatrolSpeed / WalkClipSpeed),
            Child("Run", InvestigateSpeed, InvestigateSpeed / RunClipSpeed),
            Child("Run", ChaseSpeed, ChaseSpeed / RunClipSpeed),
        };

        AnimatorState locomotion = machine.AddState("Locomotion");
        locomotion.motion = tree;
        machine.defaultState = locomotion;

        AnimatorState grab = machine.AddState("Grab");
        AnimationClip grabClip = Load("Grab");
        grab.motion = grabClip;
        grab.speed = grabClip.length / EnemyBrain.AttackWindupSeconds;

        AnimatorState stunned = machine.AddState("Stunned");
        AnimationClip stunnedClip = Load("Stunned");
        stunned.motion = stunnedClip;
        stunned.speed = stunnedClip.length / EnemyBrain.StunSeconds;

        When(locomotion, grab, QuickBlend, ("Attacking", true));
        When(grab, locomotion, Blend, ("Attacking", false), ("Stunned", false));
        When(locomotion, stunned, QuickBlend, ("Stunned", true));
        When(grab, stunned, QuickBlend, ("Stunned", true));
        When(stunned, locomotion, Blend, ("Stunned", false));

        AddLookAroundLayer(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static ChildMotion Child(string clip, float threshold, float playback) =>
        new ChildMotion { motion = Load(clip), threshold = threshold, timeScale = playback };

    static void When(AnimatorState from, AnimatorState to, float duration, params (string parameter, bool value)[] conditions)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        foreach (var (parameter, value) in conditions)
        {
            transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }
    }

    /// <summary>Turns only the head, so the Wick can look around while it walks a search. WickAnimator sets the weight.</summary>
    static void AddLookAroundLayer(AnimatorController controller)
    {
        var machine = new AnimatorStateMachine { name = "Look Around", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "Look Around",
            stateMachine = machine,
            avatarMask = BuildHeadMask(),
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,
        });
        AnimatorState look = machine.AddState("Look Around");
        look.motion = Load("LookAround");
        machine.defaultState = look;
    }

    static GameObject BuildPrefab(RuntimeAnimatorController controller)
    {
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.name = "Wick";
        var animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        model.AddComponent<WickAnimator>();
        AlertRenderer(model).shadowCastingMode = ShadowCastingMode.Off;

        int layer = LayerMask.NameToLayer("Enemy");
        foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, PrefabPath);
        Object.DestroyImmediate(model);
        return prefab;
    }

    static SkinnedMeshRenderer AlertRenderer(GameObject wick) =>
        wick.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(renderer => renderer.name == "Wick_Alert");

    static void WidenAgent()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(NavMeshSettingsPath)[0]);
        settings.FindProperty("m_Settings.Array.data[0].agentRadius").floatValue = AgentRadius;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"Put Wick In Game: NavMesh agent radius is now {NavMesh.GetSettingsByID(0).agentRadius} m.");
    }

    static GameObject LoadLampPrefab() => PrefabUtility.LoadPrefabContents(LampPath);

    /// <summary>Lit areas are widened by the enemy's body radius in LampZone, which has just grown.</summary>
    static void ResizeLitAreas(GameObject lamp)
    {
        foreach (LampZone zone in lamp.GetComponentsInChildren<LampZone>(true)) zone.SyncNavMeshVolume();
        PrefabUtility.SaveAsPrefabAsset(lamp, LampPath);
        PrefabUtility.UnloadPrefabContents(lamp);
    }

    static void PlaceInScene(string path, GameObject prefab)
    {
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (LampZone zone in roots.SelectMany(root => root.GetComponentsInChildren<LampZone>(true)))
        {
            zone.SyncNavMeshVolume();
            var volume = zone.GetComponent<NavMeshModifierVolume>();
            if (volume != null && PrefabUtility.IsPartOfPrefabInstance(volume)) PrefabUtility.RecordPrefabInstancePropertyModifications(volume);
        }

        Enemy[] enemies = roots.SelectMany(root => root.GetComponentsInChildren<Enemy>(true)).ToArray();
        foreach (Enemy enemy in enemies) PutWickOn(enemy, prefab);

        Rebake(roots);
        CheckRoutes(path, enemies);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void PutWickOn(Enemy enemy, GameObject prefab)
    {
        var old = new List<GameObject>();
        foreach (Transform child in enemy.transform)
        {
            if (OldParts.Contains(child.name)) old.Add(child.gameObject);
        }
        foreach (GameObject part in old) Object.DestroyImmediate(part);

        var wick = (GameObject)PrefabUtility.InstantiatePrefab(prefab, enemy.transform);
        wick.name = "Wick";
        wick.transform.localPosition = Vector3.zero;
        wick.transform.localRotation = Quaternion.identity;

        var serialized = new SerializedObject(enemy);
        serialized.FindProperty("alertLight").objectReferenceValue = AlertRenderer(wick);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        enemy.GetComponent<NavMeshAgent>().radius = AgentRadius;
    }

    /// <summary>The same as pressing Bake on each surface, but finished before the scene is saved.</summary>
    static void Rebake(GameObject[] roots)
    {
        foreach (NavMeshSurface surface in roots.SelectMany(root => root.GetComponentsInChildren<NavMeshSurface>(true)))
        {
            string assetPath = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : null;
            surface.BuildNavMesh();
            if (string.IsNullOrEmpty(assetPath))
            {
                string scenePath = surface.gameObject.scene.path;
                string folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scenePath), System.IO.Path.GetFileNameWithoutExtension(scenePath));
                System.IO.Directory.CreateDirectory(folder);
                assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/NavMesh-{surface.name}.asset");
            }
            else
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            EditorUtility.SetDirty(surface);
        }
    }

    /// <summary>A wider agent fits through less, so check every patrol route still joins up outside the light.</summary>
    static void CheckRoutes(string scenePath, IEnumerable<Enemy> enemies)
    {
        int dark = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName(LitArea));
        var path = new NavMeshPath();
        foreach (Enemy enemy in enemies)
        {
            var route = new SerializedObject(enemy).FindProperty("patrolRoute");
            var stops = new List<Vector3> { enemy.transform.position };
            for (int i = 0; i < route.arraySize; i++)
            {
                var point = route.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (point != null) stops.Add(point.position);
            }

            for (int i = 1; i < stops.Count; i++)
            {
                bool ok = NavMesh.SamplePosition(stops[i - 1], out NavMeshHit from, 1f, dark)
                    && NavMesh.SamplePosition(stops[i], out NavMeshHit to, 1f, dark)
                    && NavMesh.CalculatePath(from.position, to.position, dark, path)
                    && path.status == NavMeshPathStatus.PathComplete;
                if (!ok) Debug.LogWarning($"Put Wick In Game: in {scenePath}, {enemy.name} can no longer walk from stop {i - 1} to stop {i} of its route.", enemy);
            }
        }
    }

    static Transform Bone(Transform model, string name) =>
        model.GetComponentsInChildren<Transform>(true).First(t => t.name == Prefix + name);

    static void Aim(Transform bone, Transform child, Vector3 direction)
    {
        Vector3 current = (child.position - bone.position).normalized;
        bone.rotation = Quaternion.FromToRotation(current, direction.normalized) * bone.rotation;
    }
}
