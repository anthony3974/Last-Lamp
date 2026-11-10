using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: makes the way through Warehouse_01 readable in the dark, without lighting anything up.
/// The top bar of every rail (stairs and catwalk) glows faintly, and the blocked back door gets a faint outline.
/// Glow comes from Unlit materials like Lamp_Glow, with no Light components, so nothing here looks like a lit pool.
/// New objects go under "Warehouse/Highlights". Safe to run again. Delete once confirmed.
/// </summary>
public static class WayHighlights
{
    const string ScenePath = "Assets/Scenes/Warehouse_01.unity";
    const string GroupName = "Highlights";
    const string SteelPath = "Assets/Materials/Warehouse_Rusted_Steel.mat";

    // Below 1, so the lines read as faint colour and don't bloom.
    static readonly Color RailColour = new Color(0.45f, 0.85f, 1f);
    static readonly Color ExitColour = new Color(0.25f, 1f, 0.45f);
    const float RailGlow = 0.6f;
    const float ExitGlow = 0.8f;

    const float TopBarThickness = 0.1f;
    const float PlainBarThickness = 0.06f;
    const float DoorOutline = 0.12f;

    // If this marker exists when Unity reloads scripts, run once by itself (outside Play mode) and delete the marker.
    const string AutoRunMarker = "Temp/RunWayHighlights";

    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        if (!System.IO.File.Exists(AutoRunMarker)) return;
        EditorApplication.delayCall += TryAutoRun;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) TryAutoRun();
        };
    }

    static void TryAutoRun()
    {
        if (!System.IO.File.Exists(AutoRunMarker) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        System.IO.File.Delete(AutoRunMarker);
        Run();
    }

    [MenuItem("Tools/Add Way Highlights")]
    static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Add Way Highlights: leave Play mode first.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject hallObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Warehouse");
        var steel = AssetDatabase.LoadAssetAtPath<Material>(SteelPath);
        if (scene.path != ScenePath || hallObject == null || steel == null)
        {
            Debug.LogError($"Add Way Highlights: open {ScenePath} first (and check {SteelPath} exists).");
            return;
        }

        Transform hall = hallObject.transform;
        Transform door = hall.Find("Walls/Back_Door");
        if (door == null)
        {
            Debug.LogError("Add Way Highlights: Walls/Back_Door was not found.");
            return;
        }

        Material rail = GlowMaterial("Highlight_Rail", RailColour, RailGlow);
        Material exit = GlowMaterial("Highlight_Exit", ExitColour, ExitGlow);

        // Materials from earlier versions of this tool that are no longer used.
        AssetDatabase.DeleteAsset("Assets/Materials/Highlight_Exit_Door.mat");
        AssetDatabase.DeleteAsset("Assets/Materials/Highlight_Beacon.mat");

        Transform old = hall.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Transform group = new GameObject(GroupName).transform;
        group.SetParent(hall, false);

        int rails = GlowTopBars(hall, rail, steel);
        door.GetComponent<MeshRenderer>().sharedMaterial = steel;
        AddDoorOutline(door, group, exit);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"Add Way Highlights: top bar of {rails} rails and the back door outline done.");
    }

    // Same approach as Lamp_Glow: Unlit, so the colour shows in the dark regardless of lighting.
    static Material GlowMaterial(string name, Color colour, float intensity)
    {
        string path = $"Assets/Materials/{name}.mat";
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(unlit);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = unlit;
        Color tint = colour * intensity;
        tint.a = 1f;
        material.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Each rail has two bars; only the higher one glows. The lower one goes back to plain steel.
    static int GlowTopBars(Transform hall, Material glow, Material steel)
    {
        int count = 0;
        foreach (Transform rail in hall.GetComponentsInChildren<Transform>(true))
        {
            if (!rail.name.StartsWith("Rail_")) continue;
            Transform[] bars = rail.Cast<Transform>().Where(child => child.name == "Bar").ToArray();
            if (bars.Length == 0) continue;

            float top = bars.Max(bar => bar.localPosition.y);
            foreach (Transform bar in bars)
            {
                bool isTop = Mathf.Approximately(bar.localPosition.y, top);
                float thickness = isTop ? TopBarThickness : PlainBarThickness;
                bar.GetComponent<MeshRenderer>().sharedMaterial = isTop ? glow : steel;
                bar.localScale = new Vector3(thickness, thickness, bar.localScale.z);
            }

            count++;
        }

        return count;
    }

    static void AddDoorOutline(Transform door, Transform group, Material material)
    {
        Bounds bounds = door.GetComponent<MeshRenderer>().bounds;
        const float depth = 0.06f;
        float z = bounds.min.z - depth / 2f;

        Line("Door_Outline_Top", group, material,
            new Vector3(bounds.center.x, bounds.max.y - DoorOutline / 2f, z), new Vector3(bounds.size.x, DoorOutline, depth));
        Line("Door_Outline_Left", group, material,
            new Vector3(bounds.min.x + DoorOutline / 2f, bounds.center.y, z), new Vector3(DoorOutline, bounds.size.y, depth));
        Line("Door_Outline_Right", group, material,
            new Vector3(bounds.max.x - DoorOutline / 2f, bounds.center.y, z), new Vector3(DoorOutline, bounds.size.y, depth));
    }

    // A cube with no collider and no shadow, so it can't block the player, vision or the NavMesh.
    static void Line(string name, Transform parent, Material material, Vector3 position, Vector3 size)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        Object.DestroyImmediate(box.GetComponent<Collider>());
        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        box.transform.localScale = size;
        box.transform.position = position;
    }
}
