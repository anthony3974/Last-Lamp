using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-off: links Corridor_01 to Warehouse_01. Puts both levels in the build list, in order, and sets the corridor's
/// End Point to load Warehouse_01 after a 2 s hold on its finished message, which no longer offers a restart.
/// Delete this script once it has run (it stays in git history).
/// </summary>
static class LinkLevelsBuilder
{
    const string CorridorPath = "Assets/Scenes/Corridor_01.unity";
    const string WarehousePath = "Assets/Scenes/Warehouse_01.unity";

    [MenuItem("Tools/Link Levels")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(CorridorPath, true),
            new EditorBuildSettingsScene(WarehousePath, true),
        };

        var scene = EditorSceneManager.OpenScene(CorridorPath, OpenSceneMode.Single);
        var endPoint = Object.FindFirstObjectByType<EndPoint>();
        if (endPoint == null)
        {
            Debug.LogError("Link Levels: Corridor_01 has no End Point.");
            return;
        }

        var serialized = new SerializedObject(endPoint);
        serialized.FindProperty("nextLevel").stringValue = "Warehouse_01";
        serialized.FindProperty("holdSeconds").floatValue = 2f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var message = serialized.FindProperty("finishedMessage").objectReferenceValue as GameObject;
        var label = message != null ? message.GetComponentInChildren<Text>(true) : null;
        if (label != null)
        {
            Undo.RecordObject(label, "Link Levels");
            label.text = "YOU MADE IT";
            EditorUtility.SetDirty(label);
        }
        else
        {
            Debug.LogWarning("Link Levels: couldn't find the finished message's text, so it still says E  Restart.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Link Levels: build list is Corridor_01 then Warehouse_01, and the corridor's End Point loads Warehouse_01 after 2 s.");
    }
}
