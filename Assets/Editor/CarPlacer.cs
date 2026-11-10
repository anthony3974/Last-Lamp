using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: adds a street lamp beside the car on the street outside Warehouse_01. Everything it makes is a
/// normal object that can be edited by hand. Delete once the lamp is confirmed.
/// </summary>
public static class CarPlacer
{
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";

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
}
