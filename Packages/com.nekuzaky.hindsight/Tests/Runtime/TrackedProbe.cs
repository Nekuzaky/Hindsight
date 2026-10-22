using System;
using UnityEngine;

namespace Nekuzaky.Hindsight.Tests
{
    public class TrackedProbe : MonoBehaviour
    {
        public enum ProbeMode { Idle, Run, Jump }

        [HindsightTrack] public float Health;
        [HindsightTrack("Mode")] public ProbeMode CurrentMode;
        [field: HindsightTrack] public Vector2 Aim { get; set; }
        public Stats Nested;

        [Serializable]
        public struct Stats
        {
            public int Ammo;
            public string Name;
        }
    }
}
