using System.Linq;
using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool (tasks 20 and 22): adds a "Combat Sounds" child to the Player in Main, with its own Audio Source and the
/// chosen candidates for the punch swing, hit, shrugged-off punch and dodge whoosh. Delete once the sounds are settled.
/// </summary>
public static class CombatSoundsBuilder
{
    const string MainPath = "Assets/Scenes/Main.unity";
    const string Folder = "Assets/Audio/Candidates/";

    [MenuItem("Tools/Add Combat Sounds")]
    static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Add Combat Sounds: stop Play mode first.");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(MainPath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);

        Transform player = scene.GetRootGameObjects().First(root => root.name == "Player").transform;
        if (player.Find("Combat Sounds") != null)
        {
            Debug.LogError("Add Combat Sounds: the Player already has Combat Sounds.");
            return;
        }

        var holder = new GameObject("Combat Sounds");
        holder.transform.SetParent(player, false);

        // Same 3D settings as the player's own source, which plays the footsteps and landings.
        var source = holder.AddComponent<AudioSource>();
        EditorUtility.CopySerialized(player.GetComponent<AudioSource>(), source);
        source.clip = null;
        source.playOnAwake = false;
        source.loop = false;

        var sounds = new SerializedObject(holder.AddComponent<CombatSounds>());
        Clip(sounds, "swing", "Punch_Swing/Swing_08_Whistle.wav");
        Clip(sounds, "hit", "Punch_Hit/Hit_01_Meaty.wav");
        Clip(sounds, "shruggedOff", "Punch_Shrugged_Off/Shrug_01_Dull_Thud.wav");
        Clip(sounds, "dodge", "Dodge_Whoosh/Whoosh_05_Swirl.wav");
        sounds.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
        Debug.Log("Add Combat Sounds: finished. Player/Combat Sounds in Main.");
    }

    static void Clip(SerializedObject sounds, string property, string file)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + file);
        if (clip == null) Debug.LogError($"Add Combat Sounds: {Folder + file} wasn't found.");
        sounds.FindProperty(property).objectReferenceValue = clip;
    }
}
