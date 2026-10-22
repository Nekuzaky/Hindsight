using System;
using UnityEngine;

namespace Nekuzaky.Hindsight.Dev
{
    /// <summary>
    /// Development helper: forwards every Update to editor code, so the benchmark and the media capture can drive
    /// Play Mode frame by frame without coroutines. Not part of the package.
    /// </summary>
    public sealed class FrameDriver : MonoBehaviour
    {
        public static Action Tick;

        private void Update() => Tick?.Invoke();
    }
}
