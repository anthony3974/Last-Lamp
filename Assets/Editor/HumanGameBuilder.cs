using System.Collections.Generic;
using System.Linq;
using LastLamp;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off tool: imports the Mixamo clips, builds the Animator and the Human prefab, and puts him under Player/Body in
/// both levels in place of the capsule. Safe to run again. Delete once the character is confirmed in the game.
/// </summary>
public static class HumanGameBuilder
{
    const string Folder = "Assets/Art/Human";
    const string ClipFolder = Folder + "/Animations";
    const string ModelPath = Folder + "/Human.fbx";
    const string ControllerPath = Folder + "/Human.controller";
    const string MaskPath = Folder + "/Human_UpperBody.mask";
    const string PrefabPath = "Assets/Prefabs/Human.prefab";
    const string StepFunction = nameof(PlayerAnimator.FootStep);

    static readonly string[] Scenes = { "Assets/Scenes/Corridor_01.unity", "Assets/Scenes/Warehouse_01.unity" };

    // Clip speeds that make his feet match the player's real speed. Measured from each clip at his size.
    const float WalkPlayback = 3f / 2.3f;
    const float SprintPlayback = 5.5f / 4.15f;
    const float CrouchWalkPlayback = 1.5f / 1.27f;

    const float PunchClipSeconds = 1.03f;
    const float PunchCooldown = 0.6f;
    const float DodgeClipSeconds = 0.8f;

    // Written into both scenes, because the scenes keep their own copies of these. Owner, 2026-11-10: a longer dodge, so
    // the roll can be seen, at a lower speed so it covers 3.2 m instead of 2.4. A punch that reaches farther.
    const float DodgeSeconds = 0.45f;
    const float DodgeSpeed = 7f;
    const float PunchReach = 2.5f;
    const float WalkThreshold = 3f;
    const float SprintThreshold = 5.5f;
    const float CrouchThreshold = 1.5f;
    const float QuickBlend = 0.05f;
    const float Blend = 0.15f;

    /// <summary>Which root movement stays in the pose. Anything left out is dropped, because the Animator doesn't move him.</summary>
    class Clip
    {
        public string Name;
        public bool Loop;
        public bool KeepHeight = true;
        // Height that isn't kept is taken from the lower foot, so the feet stay at the controller's base while the body tucks.
        public bool HeightFromFeet;
        public bool KeepTravel = true;
        public int First = -1;
        public int Last = -1;
        public float[] Steps = { };
    }

    static readonly Clip[] Clips =
    {
        new Clip { Name = "Idle", Loop = true },
        new Clip { Name = "Walk", Loop = true, Steps = new[] { 7f / 25f, 20f / 25f } },
        new Clip { Name = "Sprint", Loop = true, Steps = new[] { 8f / 21f, 18f / 21f } },
        new Clip { Name = "CrouchIdle", Loop = true },
        new Clip { Name = "CrouchWalk", Loop = true },
        // From leaving the ground to the top. The player's real jump provides the height.
        new Clip { Name = "Jump", KeepHeight = false, HeightFromFeet = true, First = 15, Last = 24 },
        new Clip { Name = "Fall", Loop = true },
        new Clip { Name = "Punch" },
        // Only the roll, without the run before and after it. The low body has to stay, the 5 m of travel must not.
        new Clip { Name = "Dodge", KeepTravel = false, First = 17, Last = 41 },
    };

    [MenuItem("Tools/Put Human In Game")]
    public static void Build()
    {
        // The levels are opened in turn, so give the chance to save whatever is open first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (Clip clip in Clips) ImportClip(clip);
        AnimatorController controller = BuildController();
        GameObject prefab = BuildPrefab(controller);
        foreach (string scene in Scenes) PlaceInScene(scene, prefab);
        Debug.Log("Put Human In Game: done.");
    }

    static string PathOf(string clip) => $"{ClipFolder}/{clip}.fbx";

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
        // Movement that isn't kept has to be taken from the centre of mass. Mixamo's own root never moves, so taking it from
        // there ("Original") would leave all the travel in the body.
        settings.lockRootHeightY = clip.KeepHeight;
        settings.keepOriginalPositionY = clip.KeepHeight;
        settings.heightFromFeet = clip.HeightFromFeet;
        settings.lockRootPositionXZ = clip.KeepTravel;
        settings.keepOriginalPositionXZ = clip.KeepTravel;
        settings.events = clip.Steps.Select(time => new AnimationEvent { functionName = StepFunction, time = time }).ToArray();

        importer.clipAnimations = new[] { settings };
        importer.SaveAndReimport();
    }

    static AnimationClip Load(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(PathOf(name)).OfType<AnimationClip>().First(clip => clip.name == name);

    static AvatarMask BuildUpperBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }

        var legs = new[]
        {
            AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
            AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK,
        };
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            mask.SetHumanoidBodyPartActive(part, !legs.Contains(part));
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static BlendTree MakeTree(AnimatorController controller, string name, params (string clip, float threshold, float playback)[] clips)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = "Speed",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.children = clips.Select(entry => new ChildMotion
        {
            motion = Load(entry.clip),
            threshold = entry.threshold,
            timeScale = entry.playback,
        }).ToArray();
        return tree;
    }

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Crouching", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Airborne", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Punch", AnimatorControllerParameterType.Trigger);

        AnimatorControllerLayer[] layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState locomotion = machine.AddState("Locomotion");
        locomotion.motion = MakeTree(controller, "Locomotion", ("Idle", 0f, 1f), ("Walk", WalkThreshold, WalkPlayback), ("Sprint", SprintThreshold, SprintPlayback));
        locomotion.iKOnFeet = true;
        AnimatorState crouch = machine.AddState("Crouch");
        crouch.motion = MakeTree(controller, "Crouch", ("CrouchIdle", 0f, 1f), ("CrouchWalk", CrouchThreshold, CrouchWalkPlayback));
        crouch.iKOnFeet = true;
        AnimatorState jump = machine.AddState("Jump");
        jump.motion = Load("Jump");
        AnimatorState fall = machine.AddState("Fall");
        fall.motion = Load("Fall");
        AnimatorState dodge = machine.AddState("Dodge");
        dodge.motion = Load("Dodge");
        dodge.speed = DodgeClipSeconds / DodgeSeconds;
        machine.defaultState = locomotion;

        When(locomotion, crouch, Blend, ("Crouching", true));
        When(crouch, locomotion, Blend, ("Crouching", false));

        foreach (AnimatorState ground in new[] { locomotion, crouch })
        {
            Trigger(ground, jump, "Jump", QuickBlend);
            When(ground, fall, Blend, ("Airborne", true));
        }

        AnimatorStateTransition rise = jump.AddTransition(fall);
        rise.hasExitTime = true;
        rise.exitTime = 0.9f;
        rise.duration = Blend;
        // No early way back to the ground from Jump: Airborne only turns on a moment after takeoff, so it would cut the jump off.

        When(fall, locomotion, QuickBlend, ("Airborne", false), ("Crouching", false));
        When(fall, crouch, QuickBlend, ("Airborne", false), ("Crouching", true));

        AnimatorStateTransition startDodge = machine.AddAnyStateTransition(dodge);
        startDodge.AddCondition(AnimatorConditionMode.If, 0f, "Dodge");
        startDodge.duration = QuickBlend;
        startDodge.canTransitionToSelf = false;
        foreach (var (target, crouching) in new[] { (locomotion, false), (crouch, true) })
        {
            AnimatorStateTransition end = dodge.AddTransition(target);
            end.hasExitTime = true;
            end.exitTime = 0.85f;
            end.duration = QuickBlend;
            end.AddCondition(crouching ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Crouching");
        }

        AddPunchLayer(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

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

    static void Trigger(AnimatorState from, AnimatorState to, string parameter, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = duration;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    /// <summary>The punch only takes over the upper body, so he can punch while walking.</summary>
    static void AddPunchLayer(AnimatorController controller)
    {
        var machine = new AnimatorStateMachine { name = "Upper Body", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "Upper Body",
            stateMachine = machine,
            avatarMask = BuildUpperBodyMask(),
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 1f,
        });

        AnimatorState empty = machine.AddState("Empty");
        AnimatorState punch = machine.AddState("Punch");
        punch.motion = Load("Punch");
        punch.speed = PunchClipSeconds / (PunchCooldown * 0.9f);
        machine.defaultState = empty;

        Trigger(empty, punch, "Punch", QuickBlend);
        AnimatorStateTransition done = punch.AddTransition(empty);
        done.hasExitTime = true;
        done.exitTime = 0.9f;
        done.duration = Blend;
    }

    static GameObject BuildPrefab(RuntimeAnimatorController controller)
    {
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        model.name = "Human";
        var animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        model.AddComponent<CoatSkirt>();
        model.AddComponent<PlayerAnimator>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, PrefabPath);
        Object.DestroyImmediate(model);
        return prefab;
    }

    static void PlaceInScene(string path, GameObject prefab)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        GameObject player = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CharacterController>(true)).Select(c => c.gameObject).First(g => g.name == "Player");
        Transform body = player.transform.Find("Body");

        var old = new List<GameObject>();
        int layer = body.gameObject.layer;
        foreach (Transform child in body)
        {
            if (child.name == "Mesh" || child.name == "Visor" || child.name == "Human")
            {
                if (child.name == "Mesh") layer = child.gameObject.layer;
                old.Add(child.gameObject);
            }
        }
        foreach (GameObject item in old) Object.DestroyImmediate(item);

        var human = (GameObject)PrefabUtility.InstantiatePrefab(prefab, body);
        human.name = "Human";
        // The controller hovers by its skin width, so without this his feet would be off the floor.
        human.transform.localPosition = Vector3.down * player.GetComponent<CharacterController>().skinWidth;
        foreach (Transform part in human.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;

        SetFloats(player.GetComponent<PlayerController>(), ("dodgeSeconds", DodgeSeconds), ("dodgeSpeed", DodgeSpeed));
        SetFloats(player.GetComponent<PlayerCombat>(), ("punchReach", PunchReach));

        EditorSceneManager.SaveScene(scene);
    }

    static void SetFloats(Object target, params (string field, float value)[] values)
    {
        var serialized = new SerializedObject(target);
        foreach (var (field, value) in values) serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
