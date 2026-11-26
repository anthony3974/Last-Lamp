using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// One-off tool: revises the title screen built by <see cref="TitleScreenBuilder"/>.
/// In the Title scene the lamp post moves out from behind the man to his right, and gains an arm that holds the lamp
/// over him, a shade over the lamp and a faint glow that picks out the arm and the top of the post against the dark.
/// In Main the title takes a condensed serif, spaced out, with a tagline under it. Safe to run again. Delete once checked.
/// </summary>
public static class TitleScreenReviser
{
    const string MainPath = "Assets/Scenes/Main.unity";
    const string TitlePath = "Assets/Scenes/Title.unity";
    const string TitleFontPath = "Assets/Fonts/NotoSerif-CondensedLight.ttf";
    const string TaglineFontPath = "Assets/Fonts/NotoSerif-CondensedLightItalic.ttf";

    // The post stands to the man's right, clear of him from the title camera; the arm reaches back over him.
    static readonly Vector3 PostPosition = new Vector3(3.1f, 0f, 0.9f);
    const float PostHeight = 3.9f;
    const float ArmHeight = 3.8f;
    const float ArmReach = 1.3f;

    static readonly Color TitleColour = new Color(0.92f, 0.89f, 0.82f, 1f);
    // The lamp's warm colour, as on the buttons' highlight, but lighter so it reads as text.
    static readonly Color TaglineColour = new Color(0.8f, 0.62f, 0.36f, 0.9f);

    [MenuItem("Tools/Revise Title Screen")]
    public static void Run()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ReviseLamp();
        ReviseTitle();

        AssetDatabase.SaveAssets();
        Debug.Log("Revise Title Screen: done.");
    }

    // --- The Title scene ---

    static void ReviseLamp()
    {
        Scene scene = EditorSceneManager.OpenScene(TitlePath, OpenSceneMode.Single);
        Transform street = scene.GetRootGameObjects().First(root => root.name == "Street").transform;
        Transform post = street.Find("Lamp_Post");
        var steel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Warehouse_Rusted_Steel.mat");

        post.localPosition = PostPosition;

        Transform pole = post.Find("Pole");
        pole.localPosition = new Vector3(0f, PostHeight / 2f, 0f);
        pole.localScale = new Vector3(0.15f, PostHeight, 0.15f);

        // Out from the post's top towards the man, then the lamp hangs from its end.
        Box("Arm", post, new Vector3(-ArmReach / 2f, ArmHeight, 0f), new Vector3(ArmReach + 0.1f, 0.08f, 0.08f), steel);
        Vector3 head = new Vector3(-ArmReach, ArmHeight, 0f);
        Box("Shade", post, head + new Vector3(0f, -0.06f, 0f), new Vector3(0.7f, 0.06f, 0.7f), steel);

        Transform lamp = post.Find("Lamp");
        lamp.localPosition = head + new Vector3(0f, -0.14f, 0f);

        // Just enough light on the arm, the shade and the top of the post to see their shape. No shadows, so the man
        // still has only the one, from the lamp.
        Transform glow = post.Find("Glow");
        if (glow == null)
        {
            glow = new GameObject("Glow", typeof(Light)).transform;
            glow.SetParent(post, false);
        }
        glow.localPosition = new Vector3(-ArmReach * 0.6f, ArmHeight - 0.35f, -0.4f);
        var light = glow.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.65f);
        light.intensity = 1.5f;
        light.range = 1.8f;
        light.shadows = LightShadows.None;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    // --- Main ---

    static void ReviseTitle()
    {
        Scene scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Single);
        Transform hud = scene.GetRootGameObjects().First(root => root.name == "HUD").transform;
        Transform panel = hud.Find("Title Menu/Panel");

        var title = panel.Find("Title").GetComponent<Text>();
        title.font = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
        title.fontSize = 104;
        // Legacy text has no letter spacing, so thin spaces between the letters spread the title out.
        title.text = string.Join("\u2009", "LAST".ToCharArray()) + "\u2002" + string.Join("\u2009", "LAMP".ToCharArray());
        title.color = TitleColour;
        title.horizontalOverflow = HorizontalWrapMode.Overflow;
        Place(title.rectTransform, new Vector2(160f, 240f), new Vector2(1100f, 150f));
        EditorUtility.SetDirty(title);

        Transform taglineObject = panel.Find("Tagline");
        if (taglineObject == null)
        {
            var created = Object.Instantiate(title.gameObject, panel, false);
            created.name = "Tagline";
            created.transform.SetSiblingIndex(title.transform.GetSiblingIndex() + 1);
            taglineObject = created.transform;
        }
        var tagline = taglineObject.GetComponent<Text>();
        tagline.font = AssetDatabase.LoadAssetAtPath<Font>(TaglineFontPath);
        tagline.fontSize = 32;
        tagline.text = "Stay in the light.";
        tagline.color = TaglineColour;
        Place(tagline.rectTransform, new Vector2(166f, 150f), new Vector2(1100f, 50f));
        EditorUtility.SetDirty(tagline);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // Finds or makes a box under the parent.
    static void Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        Transform box = parent.Find(name);
        if (box == null)
        {
            box = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            box.name = name;
            box.SetParent(parent, false);
        }
        box.localPosition = position;
        box.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
