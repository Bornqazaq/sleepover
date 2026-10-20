using System;
using UnityEngine;

namespace Igruha.Tests
{
    // Observe the finished frame after CircusCageMascot's LateUpdate, also on
    // headless clients where WaitForEndOfFrame and render callbacks do not run.
    [DefaultExecutionOrder(400)]
    internal sealed class CircusMascotProbeSampler : MonoBehaviour
    {
        public Action Tick;
        private void LateUpdate() => Tick?.Invoke();
    }
}
