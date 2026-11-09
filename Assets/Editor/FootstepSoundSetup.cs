using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-off tool: gives the Player the owner's concrete footstep sounds and removes the old placeholder.
/// Delete once the sounds are confirmed.
/// </summary>
public static class FootstepSoundSetup
{
    const string ScenePath = "Assets/Scenes/Corridor_01.unity";
    const string OldPlaceholderPath = "Assets/Audio/Footstep.wav";

    static readonly string[] ClipPaths =
    {
        "Assets/Audio/Footstep_Concrete_1.mp3",
        "Assets/Audio/Footstep_Concrete_2.mp3",
    };

    [MenuItem("Tools/Use Concrete Footsteps")]
    static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        var player = GameObject.Find("Player");
        if (scene.path != ScenePath || player == null)
        {
            Debug.LogError($"Use Concrete Footsteps: open {ScenePath} first.");
            return;
        }

        var serialized = new SerializedObject(player.GetComponent<PlayerNoise>());
        var clips = serialized.FindProperty("footsteps");
        clips.arraySize = ClipPaths.Length;

        for (int i = 0; i < ClipPaths.Length; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPaths[i]);
            if (clip == null)
            {
                Debug.LogError($"Use Concrete Footsteps: {ClipPaths[i]} was not found.");
                return;
            }
            clips.GetArrayElementAtIndex(i).objectReferenceValue = clip;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.DeleteAsset(OldPlaceholderPath);
        Debug.Log("Use Concrete Footsteps: finished.");
    }
}
