using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: widens the corridor to 6 m and moves its lamps to alternating walls, leaving a dark lane
/// past each pool for enemies. Delete once the layout is confirmed.
/// </summary>
public static class CorridorWidener
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const float Width = 6f;
    const float Thickness = 0.2f;
    const float LampSideOffset = 2f;
    const int LampCount = 6;

    [MenuItem("Tools/Widen Corridor")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError($"Widen Corridor: open {ScenePath} first.");
            return;
        }

        float wallX = Width / 2f + Thickness / 2f;
        Transform rightWall = Require("Corridor/Wall_Right");
        if (Mathf.Approximately(rightWall.position.x, wallX))
        {
            Debug.LogError("Widen Corridor: the corridor is already 6 m wide.");
            return;
        }

        SetX(Require("Corridor/Wall_Left"), -wallX);
        SetX(rightWall, wallX);
        SetWidth(Require("Corridor/Floor"), Width + Thickness * 2f);
        SetWidth(Require("Corridor/Ceiling"), Width + Thickness * 2f);

        for (int i = 0; i < LampCount; i++)
        {
            Transform lamp = Require($"Lamps/Lamp_{i + 1:00}");
            SetX(lamp, i % 2 == 0 ? LampSideOffset : -LampSideOffset);
            PrefabUtility.RecordPrefabInstancePropertyModifications(lamp);
        }

        // This crate would otherwise narrow the dark lane beside Lamp_05.
        Transform crate = Require("Corridor/Crates/Crate_04");
        crate.position = new Vector3(1f, crate.position.y, 40f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Widen Corridor: finished.");
    }

    static Transform Require(string path)
    {
        var found = GameObject.Find(path);
        if (found == null) throw new MissingReferenceException($"Widen Corridor: {path} was not found.");
        return found.transform;
    }

    static void SetX(Transform target, float x)
    {
        Vector3 position = target.position;
        position.x = x;
        target.position = position;
    }

    static void SetWidth(Transform target, float width)
    {
        Vector3 scale = target.localScale;
        scale.x = width;
        target.localScale = scale;
    }
}
