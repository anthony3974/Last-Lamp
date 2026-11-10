using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: makes Car.prefab solid (one box collider, VisionBlocker layer) and swaps it in for the graybox
/// car on the street outside Warehouse_01. A second menu item adds a street lamp beside it. Everything it makes
/// is a normal object that can be edited by hand. Delete once the car and lamp are confirmed.
/// </summary>
public static class CarPlacer
{
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";
    const string PrefabPath = "Assets/Prefabs/Car.prefab";

    // Same spot as the graybox car. The prefab's front is +Z, so a quarter turn lays it along the road (+X).
    static readonly Vector3 Position = new Vector3(4f, 0f, -6f);
    static readonly Quaternion Rotation = Quaternion.Euler(0f, 90f, 0f);

    [MenuItem("Tools/Place Car In Warehouse")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Place Car: open {ScenePath} first.");
            return;
        }

        var street = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Street");
        if (street == null)
        {
            Debug.LogError("Place Car: no Street object in the scene.");
            return;
        }

        Transform old = street.transform.Find("Car");
        if (old != null && PrefabUtility.IsPartOfPrefabInstance(old))
        {
            Debug.LogError("Place Car: the real car is already in the scene.");
            return;
        }

        int blocker = LayerMask.NameToLayer("VisionBlocker");
        if (blocker < 0 || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError("Place Car: the VisionBlocker layer or Car.prefab was not found.");
            return;
        }

        MakeSolid(blocker);

        if (old != null) Object.DestroyImmediate(old.gameObject);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, street.transform);
        car.name = "Car";
        car.transform.SetPositionAndRotation(Position, Rotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(car.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Place Car: finished.");
    }

    // On the kerb beside the car's flank, so its pool lights the car and the pavement. The existing Street_Lamp
    // is 12 m further along the road, so the two pools don't overlap.
    static readonly Vector3 LampPosition = new Vector3(4f, 0f, -3.6f);

    [MenuItem("Tools/Add Street Lamp By Car")]
    static void AddLamp()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Add Street Lamp: open {ScenePath} first.");
            return;
        }

        var posts = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Lamp Posts");
        Transform original = posts != null ? posts.transform.Find("Street_Lamp") : null;
        if (original == null)
        {
            Debug.LogError("Add Street Lamp: Lamp Posts/Street_Lamp was not found.");
            return;
        }

        if (posts.transform.Find("Street_Lamp_Car") != null)
        {
            Debug.LogError("Add Street Lamp: the lamp by the car already exists.");
            return;
        }

        // A copy of the existing street lamp (post, lamp prefab and its light settings), so it matches.
        GameObject lamp = Object.Instantiate(original.gameObject, posts.transform);
        lamp.name = "Street_Lamp_Car";
        lamp.transform.position = LampPosition;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Add Street Lamp: finished.");
    }

    static void MakeSolid(int blocker)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = blocker;

            if (root.GetComponent<BoxCollider>() == null)
            {
                Bounds bounds = root.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                var box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
