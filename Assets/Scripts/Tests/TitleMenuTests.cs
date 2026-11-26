using System.Linq;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastLamp.Tests
{
    public class TitleMenuTests
    {
        const string MainPath = "Assets/Scenes/Main.unity";
        const string TitlePath = "Assets/Scenes/Title.unity";

        Scene main;
        Scene title;
        bool openedMain;
        bool openedTitle;

        [OneTimeSetUp]
        public void OpenScenes()
        {
            // Either may already be open in the editor; if so it is left open afterwards.
            main = SceneManager.GetSceneByPath(MainPath);
            openedMain = !main.isLoaded;
            if (openedMain) main = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);

            title = SceneManager.GetSceneByPath(TitlePath);
            openedTitle = !title.isLoaded;
            if (openedTitle) title = EditorSceneManager.OpenScene(TitlePath, OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void CloseScenes()
        {
            if (openedTitle) EditorSceneManager.CloseScene(title, true);
            if (openedMain) EditorSceneManager.CloseScene(main, true);
        }

        static T[] In<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        static SerializedObject Fields(Object target) => new SerializedObject(target);

        [Test]
        public void BuildOpensOnMainWithTheTitleInTheList()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Assert.That(scenes[0].path, Is.EqualTo(MainPath), "A build opens on the first scene in the list.");
            Assert.That(scenes.Select(scene => scene.path), Does.Contain(TitlePath));
        }

        [Test]
        public void LoaderOpensOnTitleAndNewGameStartsTheCorridor()
        {
            LevelLoader loader = In<LevelLoader>(main).Single();
            SerializedObject fields = Fields(loader);
            Assert.That(fields.FindProperty("titleScene").stringValue, Is.EqualTo(title.name));
            Assert.That(fields.FindProperty("firstLevel").stringValue, Is.EqualTo("Corridor_01"));
            Assert.That(fields.FindProperty("titleMenu").objectReferenceValue, Is.EqualTo(In<TitleMenu>(main).Single()));
        }

        [Test]
        public void TitleSceneHasAStartAndItsOwnCamera()
        {
            Level level = In<Level>(title).Single();
            Assert.That(Fields(level).FindProperty("playerStart").objectReferenceValue, Is.Not.Null);

            CinemachineCamera titleCamera = In<CinemachineCamera>(title).Single();
            CinemachineCamera follow = In<CinemachineCamera>(main).Single();
            Assert.That((int)titleCamera.Priority, Is.GreaterThan((int)follow.Priority), "The title camera must win while the title is loaded.");

            CinemachineBrain brain = In<CinemachineBrain>(main).Single();
            Assert.That(brain.DefaultBlend.Style, Is.EqualTo(CinemachineBlendDefinition.Styles.Cut), "No camera sweep after the fade.");
        }

        [TestCase("New Game", nameof(TitleMenu.NewGame))]
        [TestCase("Quit", nameof(TitleMenu.Quit))]
        public void TitleButtonDoesItsJob(string buttonName, string method)
        {
            TitleMenu menu = In<TitleMenu>(main).Single();
            Button button = menu.GetComponentsInChildren<Button>(true).Single(candidate => candidate.name == buttonName);

            Assert.That(button.interactable, Is.True);
            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(button.onClick.GetPersistentTarget(0), Is.EqualTo(menu));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo(method));
        }

        [Test]
        public void ContinueIsGreyedOutUntilThereAreSaves()
        {
            TitleMenu menu = In<TitleMenu>(main).Single();
            Button button = menu.GetComponentsInChildren<Button>(true).Single(candidate => candidate.name == "Continue");
            Assert.That(button.interactable, Is.False);
            Assert.That(button.GetComponent<DimLabelWhenOff>(), Is.Not.Null, "Its text should grey out too.");
        }

        [Test]
        public void NewGameIsPickedFirst()
        {
            TitleMenu menu = In<TitleMenu>(main).Single();
            var first = (Selectable)Fields(menu).FindProperty("firstButton").objectReferenceValue;
            Assert.That(first.name, Is.EqualTo("New Game"));
        }

        [Test]
        public void TitleHidesThePlayHud()
        {
            Hud hud = In<Hud>(main).Single();
            SerializedProperty playOnly = Fields(hud).FindProperty("playOnly");
            var names = Enumerable.Range(0, playOnly.arraySize)
                .Select(i => ((GameObject)playOnly.GetArrayElementAtIndex(i).objectReferenceValue).name)
                .ToArray();
            Assert.That(names, Does.Contain("Lives"));
            Assert.That(names, Does.Contain("Visibility Cue"));
        }
    }
}
