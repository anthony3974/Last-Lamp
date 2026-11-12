using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off (task 27): makes the always-on flashlight dimmer in both levels, from intensity 12 to 8, and raises the
/// bright multiplier from 2.5 to 3.75 so the bright beam (F) stays at 30, as before. Range is unchanged.
/// Delete this script once it has run (it stays in git history).
/// </summary>
static class DimFlashlightBuilder
{
    const float NormalIntensity = 8f;
    const float BrightIntensity = 30f;

    static readonly string[] Scenes =
    {
        "Assets/Scenes/Corridor_01.unity",
        "Assets/Scenes/Warehouse_01.unity",
    };

    [MenuItem("Tools/Dim Flashlight")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        foreach (string path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var flashlight = Object.FindFirstObjectByType<PlayerFlashlight>();
            var beam = flashlight != null ? flashlight.GetComponent<Light>() : null;
            if (beam == null)
            {
                Debug.LogError($"Dim Flashlight: no PlayerFlashlight with a Light in {path}.");
                continue;
            }

            float before = beam.intensity;
            Undo.RecordObject(beam, "Dim Flashlight");
            beam.intensity = NormalIntensity;
            EditorUtility.SetDirty(beam);

            var serialized = new SerializedObject(flashlight);
            serialized.FindProperty("brightIntensityMultiplier").floatValue = BrightIntensity / NormalIntensity;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Dim Flashlight: {path} normal beam {before} to {NormalIntensity}, bright beam stays {BrightIntensity}.");
        }
    }
}
