using System;
using UnityEngine;

namespace Nekuzaky.Hindsight.Dev
{
    public sealed class FrameDriver : MonoBehaviour
    {
        public static Action Tick;

        private void Update() => Tick?.Invoke();
    }
}
