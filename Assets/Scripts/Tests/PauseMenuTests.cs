using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LastLamp.Tests
{
    public class PauseMenuTests
    {
        const string MainPath = "Assets/Scenes/Main.unity";
        const string ButtonPath = "Assets/Prefabs/UIButton.prefab";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";

        Scene main;
        bool openedHere;

        [OneTimeSetUp]
        public void OpenMain()
        {
            // Main may already be open in the editor; if so it is left open afterwards.
            main = SceneManager.GetSceneByPath(MainPath);
            openedHere = !main.isLoaded;
            if (openedHere) main = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void CloseMain()
        {
            if (openedHere) EditorSceneManager.CloseScene(main, true);
        }

        T[] InMain<T>() where T : Component =>
            main.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

        [Test]
        public void EscapePauses()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            InputAction pause = actions.FindAction("UI/Pause", true);
            Assert.That(pause.bindings.Select(binding => binding.path), Does.Contain("<Keyboard>/escape"));
        }

        [Test]
        public void SharedButtonWorksWithMouseAndKeyboard()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath);
            Assert.That(prefab, Is.Not.Null);

            var button = prefab.GetComponent<Button>();
            Assert.That(button, Is.Not.Null);
            Assert.That(prefab.GetComponent<SelectOnHover>(), Is.Not.Null, "Pointing at a button should pick it, as the keys do.");
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.Vertical));
            Assert.That(button.colors.selectedColor, Is.Not.EqualTo(button.colors.normalColor), "The keyboard's pick must show.");
            Assert.That(prefab.GetComponentInChildren<Text>(), Is.Not.Null);
        }

        [Test]
        public void EveryButtonInMainIsTheSharedPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPath);
            Button[] buttons = InMain<Button>();
            Assert.That(buttons, Is.Not.Empty);

            foreach (Button button in buttons)
            {
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(button.gameObject), Is.EqualTo(prefab), button.name);
            }
        }

        [TestCase("Resume", nameof(PauseMenu.Resume))]
        [TestCase("Restart", nameof(PauseMenu.RestartFromCheckpoint))]
        [TestCase("Quit", nameof(PauseMenu.Quit))]
        public void PauseMenuButtonDoesItsJob(string buttonName, string method)
        {
            PauseMenu menu = InMain<PauseMenu>().Single();
            Button button = menu.GetComponentsInChildren<Button>(true).Single(candidate => candidate.name == buttonName);

            Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(button.onClick.GetPersistentTarget(0), Is.EqualTo(menu));
            Assert.That(button.onClick.GetPersistentMethodName(0), Is.EqualTo(method));
        }

        [Test]
        public void MainTakesMouseAndKeyboard()
        {
            EventSystem events = InMain<EventSystem>().Single();
            var module = events.GetComponent<InputSystemUIInputModule>();
            Assert.That(module, Is.Not.Null);
            Assert.That(module.actionsAsset, Is.EqualTo(AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath)));
            Assert.That(module.point?.action?.name, Is.EqualTo("Point"));
            Assert.That(module.leftClick?.action?.name, Is.EqualTo("Click"));
            Assert.That(module.move?.action?.name, Is.EqualTo("Navigate"));
            Assert.That(module.submit?.action?.name, Is.EqualTo("Submit"));

            PauseMenu menu = InMain<PauseMenu>().Single();
            Assert.That(menu.GetComponentInParent<Canvas>().GetComponent<GraphicRaycaster>(), Is.Not.Null);
        }
    }
}
