using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: makes MonsterStage_01 as a copy of Warehouse_01, so it keeps the player, camera, HUD and noir lighting,
/// then shrinks the shell to one room with a marked spot for the realistic monster model.
/// Everything it makes is a normal object that can be edited by hand. Delete once the scene is confirmed.
/// </summary>
public static class MonsterStageBuilder
{
    const string SourcePath = "Assets/Scenes/Warehouse_01.unity";
    const string ScenePath = "Assets/Scenes/MonsterStage_01.unity";
    const string BodyMaterialPath = "Assets/Materials/Monster_Body.mat";

    const float Width = 14f;
    const float Length = 20f;
    const float Height = 5f;
    const float Thickness = 0.2f;

    // Same body size as the graybox monsters, so a model can be matched to the NavMeshAgent they use.
    const float StandInHeight = 2.1f;
    const float StandInWidth = 0.8f;

    static readonly Vector3 MonsterPlace = new Vector3(0f, 0f, 12f);

    // Close enough that the monster's head is still inside the lamp's cone.
    static readonly Vector3 LampPlace = new Vector3(-1f, 0f, 12f);

    [MenuItem("Tools/Build Monster Stage Scene")]
    static void Run()
    {
        var body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);
        if (body == null)
        {
            Debug.LogError("Build Monster Stage Scene: the Monster_Body material was not found.");
            return;
        }

        if (AssetDatabase.LoadMainAssetAtPath(ScenePath) != null)
        {
            Debug.LogError($"Build Monster Stage Scene: {ScenePath} already exists.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!AssetDatabase.CopyAsset(SourcePath, ScenePath))
        {
            Debug.LogError($"Build Monster Stage Scene: could not copy {SourcePath}.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        ResizeShell(scene);
        KeepOneLampPost(scene);
        BuildMonsterSpot(scene, body);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Build Monster Stage Scene: finished.");
    }

    static GameObject Root(Scene scene, string name)
    {
        return scene.GetRootGameObjects().First(root => root.name == name);
    }

    static void ResizeShell(Scene scene)
    {
        GameObject shell = Root(scene, "Warehouse");
        shell.name = "Stage";
        Transform parent = shell.transform;

        float middleZ = Length / 2f;
        float wallY = Height / 2f;
        float sideX = (Width + Thickness) / 2f;
        float outerWidth = Width + Thickness * 2f;
        float outerLength = Length + Thickness * 2f;

        Place(parent, "Floor", new Vector3(0f, -Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, outerLength));
        Place(parent, "Roof", new Vector3(0f, Height + Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, outerLength));
        Place(parent, "Wall_Left", new Vector3(-sideX, wallY, middleZ), new Vector3(Thickness, Height, outerLength));
        Place(parent, "Wall_Right", new Vector3(sideX, wallY, middleZ), new Vector3(Thickness, Height, outerLength));
        Place(parent, "Wall_Near", new Vector3(0f, wallY, -Thickness / 2f), new Vector3(Width, Height, Thickness));
        Place(parent, "Wall_Far", new Vector3(0f, wallY, Length + Thickness / 2f), new Vector3(Width, Height, Thickness));
    }

    static void Place(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        Transform part = parent.Find(name);
        part.localPosition = position;
        part.localScale = scale;
    }

    // One lamp leaves the rest of the room dark, so the model can be judged both in a pool and by flashlight.
    static void KeepOneLampPost(Scene scene)
    {
        Transform group = Root(scene, "Lamp Posts").transform;
        foreach (string name in new[] { "Lamp_Post_02", "Lamp_Post_03", "Lamp_Post_04" })
            Object.DestroyImmediate(group.Find(name).gameObject);

        group.Find("Lamp_Post_01").position = LampPlace;
    }

    // Turned to face the player's start, so a model placed under it with zero rotation looks at the player.
    static void BuildMonsterSpot(Scene scene, Material body)
    {
        var spot = new GameObject("Monster Spot");
        SceneManager.MoveGameObjectToScene(spot, scene);
        spot.transform.SetPositionAndRotation(MonsterPlace, Quaternion.Euler(0f, 180f, 0f));

        var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        standIn.name = "Stand-in (delete when the model is in)";
        Object.DestroyImmediate(standIn.GetComponent<Collider>());
        standIn.transform.SetParent(spot.transform, false);
        standIn.transform.localPosition = Vector3.up * (StandInHeight / 2f);
        standIn.transform.localScale = new Vector3(StandInWidth, StandInHeight / 2f, StandInWidth);
        standIn.GetComponent<MeshRenderer>().sharedMaterial = body;
    }
}
