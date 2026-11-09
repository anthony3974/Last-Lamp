using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: adds the player's dim flashlight as a normal Light object under the Camera Target.
/// Delete once the flashlight is confirmed.
/// </summary>
public static class FlashlightBuilder
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";

    [MenuItem("Tools/Add Flashlight")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        var target = GameObject.Find("Player/Camera Target");
        if (scene.path != ScenePath || target == null)
        {
            Debug.LogError($"Add Flashlight: open {ScenePath} first.");
            return;
        }

        if (target.transform.Find("Flashlight") != null)
        {
            Debug.LogError("Add Flashlight: the flashlight already exists.");
            return;
        }

        var flashlight = new GameObject("Flashlight");
        flashlight.layer = LayerMask.NameToLayer("Player");
        flashlight.transform.SetParent(target.transform, false);
        flashlight.transform.localPosition = new Vector3(0.25f, -0.25f, 0.4f);

        var beam = flashlight.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.color = new Color(1f, 0.93f, 0.8f);
        beam.intensity = 4f;
        beam.range = 9f;
        beam.spotAngle = 45f;
        beam.innerSpotAngle = 25f;
        beam.shadows = LightShadows.Hard;
        beam.lightmapBakeType = LightmapBakeType.Realtime;

        flashlight.AddComponent<PlayerFlashlight>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Add Flashlight: finished.");
    }
}
