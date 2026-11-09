using NUnit.Framework;
using UnityEngine;

namespace LastLamp.Tests
{
    public class ProjectSetupTests
    {
        [TestCase("Player")]
        [TestCase("Enemy")]
        [TestCase("VisionBlocker")]
        [TestCase("Interactable")]
        public void GameLayerExists(string layerName)
        {
            Assert.That(LayerMask.NameToLayer(layerName), Is.GreaterThanOrEqualTo(8));
        }
    }
}
