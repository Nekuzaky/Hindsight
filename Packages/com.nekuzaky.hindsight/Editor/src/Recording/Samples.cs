using System.Runtime.InteropServices;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TransformSample
    {
        #region Public

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static TransformSample Unset => new()
        {
            Position = new Vector3(float.NaN, float.NaN, float.NaN),
            Rotation = new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN),
            Scale = new Vector3(float.NaN, float.NaN, float.NaN)
        };

        public readonly bool IsSet => !float.IsNaN(Position.x);

        #endregion


        #region Main API

        public static bool Same(in TransformSample a, in TransformSample b)
        {
            return a.Position.x == b.Position.x && a.Position.y == b.Position.y && a.Position.z == b.Position.z
                && a.Rotation.x == b.Rotation.x && a.Rotation.y == b.Rotation.y
                && a.Rotation.z == b.Rotation.z && a.Rotation.w == b.Rotation.w
                && a.Scale.x == b.Scale.x && a.Scale.y == b.Scale.y && a.Scale.z == b.Scale.z;
        }

        #endregion
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ValueDelta
    {
        public int Stream;
        public Vector4 Value;
    }

    internal static class ValueSample
    {
        public static Vector4 Unset => new(float.NaN, float.NaN, float.NaN, float.NaN);

        public static bool IsSet(in Vector4 value) => !float.IsNaN(value.x);

        public static bool Same(in Vector4 a, in Vector4 b)
        {
            return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        }
    }

    internal readonly struct FrameClock
    {
        public FrameClock(int gameFrame, double gameTime, double realTime, float timeScale)
        {
            GameFrame = gameFrame;
            GameTime = gameTime;
            RealTime = realTime;
            TimeScale = timeScale;
        }

        public int GameFrame { get; }
        public double GameTime { get; }
        public double RealTime { get; }
        public float TimeScale { get; }

        public static FrameClock Now => new(Time.frameCount, Time.timeAsDouble, Time.unscaledTimeAsDouble, Time.timeScale);
    }

    internal struct FrameRecord
    {
        public int Sequence;
        public int GameFrame;
        public double GameTime;
        public double RealTime;
        public float TimeScale;
        public long TransformStart;
        public int TransformCount;
        public int TransformWords;
        public long ValueStart;
        public int ValueCount;
        public bool Committed;
    }
}
