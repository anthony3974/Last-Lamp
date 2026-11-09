using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: makes Warehouse_01 as a copy of Corridor_01, so it keeps the player, camera, HUD and noir lighting,
/// then swaps the corridor for an empty warehouse shell to design in.
/// Everything it makes is a normal object that can be edited by hand. Delete once the scene is confirmed.
/// </summary>
public static class WarehouseSceneBuilder
{
    const string SourcePath = "Assets/Scenes/Corridor_01.unity";
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";
    const string WallMaterialPath = "Assets/Materials/Graybox_Wall.mat";
    const string FloorMaterialPath = "Assets/Materials/Graybox_Floor.mat";

    const float Width = 30f;
    const float Length = 50f;
    const float Height = 8f;
    const float Thickness = 0.2f;

    static readonly Vector3 PlayerStart = new Vector3(0f, 0.05f, 4f);

    static readonly string[] CorridorOnly =
    {
        "Corridor", "Yard_Start", "Yard_Finish", "Lamps", "Hiding Spots", "Cans",
        "Monster_01", "Monster_02", "Patrol Route 01", "Patrol Route 02", "End Point",
    };

    static readonly Vector3[] LampPostPlaces =
    {
        new Vector3(-8f, 0f, 12f),
        new Vector3(8f, 0f, 20f),
        new Vector3(-8f, 0f, 32f),
        new Vector3(8f, 0f, 40f),
    };

    [MenuItem("Tools/Build Warehouse Scene")]
    static void Run()
    {
        var wall = AssetDatabase.LoadAssetAtPath<Material>(WallMaterialPath);
        var floor = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        if (wall == null || floor == null)
        {
            Debug.LogError("Build Warehouse Scene: the graybox materials were not found.");
            return;
        }

        if (AssetDatabase.LoadMainAssetAtPath(ScenePath) != null)
        {
            Debug.LogError($"Build Warehouse Scene: {ScenePath} already exists.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!AssetDatabase.CopyAsset(SourcePath, ScenePath))
        {
            Debug.LogError($"Build Warehouse Scene: could not copy {SourcePath}.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        BuildShell(scene, wall, floor);
        MoveLampPosts(scene);
        RemoveCorridorObjects(scene);
        KeepOneCheckpoint(scene);
        MovePlayerToStart(scene);
        ClearNavMesh(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Build Warehouse Scene: finished.");
    }

    static GameObject Root(Scene scene, string name)
    {
        return scene.GetRootGameObjects().First(root => root.name == name);
    }

    static void BuildShell(Scene scene, Material wall, Material floor)
    {
        var shell = new GameObject("Warehouse");
        SceneManager.MoveGameObjectToScene(shell, scene);
        Transform parent = shell.transform;
        int blocker = LayerMask.NameToLayer("VisionBlocker");

        float middleZ = Length / 2f;
        float wallY = Height / 2f;
        float sideX = (Width + Thickness) / 2f;
        float outerWidth = Width + Thickness * 2f;
        float outerLength = Length + Thickness * 2f;

        Box("Floor", parent, new Vector3(0f, -Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, outerLength), floor, 0);
        Box("Roof", parent, new Vector3(0f, Height + Thickness / 2f, middleZ), new Vector3(outerWidth, Thickness, outerLength), wall, blocker);
        Box("Wall_Left", parent, new Vector3(-sideX, wallY, middleZ), new Vector3(Thickness, Height, outerLength), wall, blocker);
        Box("Wall_Right", parent, new Vector3(sideX, wallY, middleZ), new Vector3(Thickness, Height, outerLength), wall, blocker);
        Box("Wall_Near", parent, new Vector3(0f, wallY, -Thickness / 2f), new Vector3(Width, Height, Thickness), wall, blocker);
        Box("Wall_Far", parent, new Vector3(0f, wallY, Length + Thickness / 2f), new Vector3(Width, Height, Thickness), wall, blocker);
    }

    static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, int layer)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = layer;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = scale;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Reuses the yard's lamp posts, so the warehouse has light to work by from the start.
    static void MoveLampPosts(Scene scene)
    {
        Transform yardStart = Root(scene, "Yard_Start").transform;
        Transform yardFinish = Root(scene, "Yard_Finish").transform;
        Transform[] posts =
        {
            yardStart.Find("Lamp_Post_01"),
            yardStart.Find("Lamp_Post_02"),
            yardFinish.Find("Lamp_Post_01"),
            yardFinish.Find("Lamp_Post_02"),
        };

        var group = new GameObject("Lamp Posts").transform;
        SceneManager.MoveGameObjectToScene(group.gameObject, scene);

        for (int i = 0; i < posts.Length; i++)
        {
            posts[i].name = $"Lamp_Post_{i + 1:00}";
            posts[i].SetParent(group, false);
            posts[i].position = LampPostPlaces[i];
        }
    }

    static void RemoveCorridorObjects(Scene scene)
    {
        foreach (string name in CorridorOnly) Object.DestroyImmediate(Root(scene, name));
    }

    // PlayerLives points at the first checkpoint, so that one stays and moves to the new start.
    static void KeepOneCheckpoint(Scene scene)
    {
        Transform group = Root(scene, "Checkpoints").transform;
        Object.DestroyImmediate(group.Find("Checkpoint_02").gameObject);
        group.Find("Checkpoint_01").position = PlayerStart;
    }

    static void MovePlayerToStart(Scene scene)
    {
        Transform player = Root(scene, "Player").transform;
        Vector3 move = PlayerStart - player.position;

        player.position += move;
        Root(scene, "Main Camera").transform.position += move;
        Root(scene, "Player Follow Camera").transform.position += move;
    }

    // The copied surface still points at the corridor's NavMesh. The warehouse gets its own bake once it has a layout.
    static void ClearNavMesh(Scene scene)
    {
        var surface = Root(scene, "Nav Surface").GetComponent<NavMeshSurface>();
        surface.RemoveData();
        surface.navMeshData = null;
        EditorUtility.SetDirty(surface);
    }
}
