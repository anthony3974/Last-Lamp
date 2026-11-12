using System.Collections.Generic;
using LastLamp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Pressing Play with a level open plays it the way the game does: the Main scene loads first, with the player, camera,
/// HUD and the night's light, then the open level loads alongside it with the player on its start. With several levels
/// open, all of them load and the player starts in the active one. With Main open on its own, the game starts at the
/// first level, as a build does. Any other scene plays as usual.
/// Unity plays the scenes as saved, so it asks to save open changes first.
/// </summary>
[InitializeOnLoad]
static class PlayFromMain
{
    const string MainPath = "Assets/Scenes/Main.unity";

    static PlayFromMain()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => Refresh();
        EditorSceneManager.sceneClosed += scene => Refresh();
        EditorSceneManager.sceneSaved += scene => Refresh();
        EditorSceneManager.newSceneCreated += (scene, setup, mode) => Refresh();
        EditorSceneManager.activeSceneChangedInEditMode += (from, to) => Refresh();
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.ExitingEditMode) Refresh();
        };
        EditorApplication.delayCall += Refresh;
    }

    static void Refresh()
    {
        if (EditorApplication.isPlaying) return;

        bool mainOpen = false;
        var levels = new List<string>();
        Scene active = SceneManager.GetActiveScene();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;

            if (scene.path == MainPath) mainOpen = true;
            else if (HasLevel(scene)) levels.Insert(scene == active ? 0 : levels.Count, scene.path);
        }

        bool game = mainOpen || levels.Count > 0;
        EditorSceneManager.playModeStartScene = game ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MainPath) : null;
        SessionState.SetString(LevelLoader.EditorLevelsKey, string.Join(";", levels));
    }

    static bool HasLevel(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<Level>() != null) return true;
        }
        return false;
    }
}
