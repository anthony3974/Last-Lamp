using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off: adds Checkpoint_02 to Warehouse_01 in the middle of skylight S1's pool, the lit pool nearest the stairs.
/// Delete this script once the checkpoint is saved in the scene (it stays in git history).
/// </summary>
static class StairsCheckpointBuilder
{
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";
    const string PrefabPath = "Assets/Prefabs/Checkpoint.prefab";
    const string Name = "Checkpoint_02";

    [MenuItem("Tools/Add Stairs Checkpoint")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var parent = GameObject.Find("Checkpoints");
        var pool = GameObject.Find("Light Pools/LightPool_S1");
        if (parent == null || pool == null)
        {
            Debug.LogError("Add Stairs Checkpoint: couldn't find Checkpoints or Light Pools/LightPool_S1 in Warehouse_01.");
            return;
        }

        var old = parent.transform.Find(Name);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // Stand it on whatever floor is under the middle of the pool, just above it like Checkpoint_01.
        Vector3 centre = pool.transform.position;
        Vector3 position = new Vector3(centre.x, 0.05f, centre.z);
        if (Physics.Raycast(centre, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            position.y = hit.point.y + 0.05f;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var checkpoint = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        checkpoint.name = Name;
        checkpoint.transform.position = position;
        Undo.RegisterCreatedObjectUndo(checkpoint, "Add Stairs Checkpoint");

        if (checkpoint.GetComponent<Checkpoint>() == null)
        {
            Debug.LogError("Add Stairs Checkpoint: the Checkpoint prefab has no Checkpoint component.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = checkpoint;
        Debug.Log($"Add Stairs Checkpoint: {Name} placed at {position} in the middle of LightPool_S1.");
    }
}
