using System.Reflection;
using Igruha.Core.UI;
using NUnit.Framework;
using UnityEngine;

namespace Igruha.Tests
{
    public sealed class PauseScreenTests
    {
        private GameObject root;
        private PauseScreen pause;
        private float originalTime;
        private static readonly MethodInfo Open = typeof(PauseScreen).GetMethod("Pause", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp] public void SetUp()
        {
            originalTime = Time.timeScale;
            root = new GameObject("Pause test");
            pause = root.AddComponent<PauseScreen>();
        }
        [TearDown] public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            Time.timeScale = originalTime;
        }
        [Test] public void ResumeRestoresPriorTimeScale()
        {
            Time.timeScale = .5f;
            Open.Invoke(pause,null);
            Assert.That(Time.timeScale,Is.Zero);
            Assert.That(pause.IsPaused,Is.True);
            pause.Resume();
            Assert.That(Time.timeScale,Is.EqualTo(.5f));
            Assert.That(pause.IsPaused,Is.False);
        }
        [Test] public void RepeatedPauseDoesNotOverwritePreviousTime()
        {
            Time.timeScale = .75f;
            Open.Invoke(pause,null); Open.Invoke(pause,null);
            pause.Resume();
            Assert.That(Time.timeScale,Is.EqualTo(.75f));
        }
        [Test] public void ResumeWithoutPauseDoesNotChangeTime()
        {
            Time.timeScale = .25f;
            pause.Resume();
            Assert.That(Time.timeScale,Is.EqualTo(.25f));
        }
    }
}
