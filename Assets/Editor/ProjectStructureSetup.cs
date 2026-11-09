using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off tool for PLAN.md task 3: adds the game layers and renames the scene.
/// Delete once the settings are confirmed.
/// </summary>
public static class ProjectStructureSetup
{
    const string OldScene = "Assets/Scenes/SampleScene.unity";
    const string NewScene = "Assets/Scenes/Corridor_01.unity";
    const int FirstUserLayer = 8;

    static readonly string[] Layers = { "Player", "Enemy", "VisionBlocker", "Interactable" };

    [MenuItem("Tools/Project Structure Setup")]
    static void Run()
    {
        AddLayers();
        RenameScene();
        AssetDatabase.SaveAssets();
        Debug.Log("Project Structure Setup: finished.");
    }

    static void AddLayers()
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var slots = tagManager.FindProperty("layers");

        foreach (var layer in Layers)
        {
            if (LayerMask.NameToLayer(layer) >= 0) continue;

            bool placed = false;
            for (int i = FirstUserLayer; i < slots.arraySize && !placed; i++)
            {
                var slot = slots.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layer;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                placed = true;
            }

            if (!placed) Debug.LogError($"Project Structure Setup: no free layer slot for {layer}.");
        }
    }

    static void RenameScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OldScene) != null)
        {
            string error = AssetDatabase.RenameAsset(OldScene, "Corridor_01");
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"Project Structure Setup: scene rename failed: {error}");
                return;
            }
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(NewScene, true) };
    }
}
