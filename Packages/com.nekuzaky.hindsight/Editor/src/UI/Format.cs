using System;
using System.Globalization;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>Text formatting for the UI. Always uses the invariant culture so numbers read the same everywhere.</summary>
    internal static class Format
    {
        #region Public

        public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>Signed offset from now, for example "-2.315 s" or "now".</summary>
        public static string Offset(double seconds)
        {
            if (Math.Abs(seconds) < 0.0005d)
                return "now";
            return seconds.ToString(Math.Abs(seconds) >= 100d ? "0.0" : "0.000", Culture) + " s";
        }

        /// <summary>Short label for a ruler tick, using as many decimals as the tick step needs.</summary>
        public static string Tick(double seconds, double step)
        {
            if (Math.Abs(seconds) < step * 0.25d)
                return "now";
            var pattern = step >= 1d ? "0" : step >= 0.1d ? "0.0" : "0.00";
            return seconds.ToString(pattern, Culture) + "s";
        }

        public static string Integer(long value) => value.ToString("N0", Culture);

        public static string Bytes(long bytes)
        {
            if (bytes >= 1L << 30)
                return (bytes / (double)(1L << 30)).ToString("0.00", Culture) + " GB";
            if (bytes >= 1L << 20)
                return (bytes / (double)(1L << 20)).ToString("0.0", Culture) + " MB";
            return (bytes / 1024d).ToString("0", Culture) + " KB";
        }

        public static string Milliseconds(double milliseconds)
        {
            return milliseconds < 0.1d
                ? milliseconds.ToString("0.000", Culture) + " ms"
                : milliseconds.ToString("0.00", Culture) + " ms";
        }

        public static string Number(float value) => value.ToString("0.###", Culture);

        public static string Vector(Vector3 value) => $"{Number(value.x)}, {Number(value.y)}, {Number(value.z)}";

        public static string Vector(Vector2 value) => $"{Number(value.x)}, {Number(value.y)}";

        /// <summary>Formats a recorded value the way its source field reads.</summary>
        public static string Value(Vector4 value, ValueFormat format, Type enumType)
        {
            if (!ValueSample.IsSet(value))
                return "—";

            switch (format)
            {
                case ValueFormat.Integer:
                    return ((long)value.x).ToString(Culture);
                case ValueFormat.Boolean:
                    return value.x > 0.5f ? "true" : "false";
                case ValueFormat.Enum:
                    return enumType != null ? Enum.ToObject(enumType, (long)value.x).ToString() : Number(value.x);
                case ValueFormat.Vector2:
                    return Vector((Vector2)value);
                case ValueFormat.Vector3:
                case ValueFormat.AngularVelocity:
                    return Vector((Vector3)value);
                case ValueFormat.LinearVelocity:
                    return $"{Number(((Vector3)value).magnitude)} m/s   ({Vector((Vector3)value)})";
                case ValueFormat.AngularVelocity2D:
                    return Number(value.z) + " °/s";
                case ValueFormat.Rotation:
                    return Vector(new Quaternion(value.x, value.y, value.z, value.w).eulerAngles);
                case ValueFormat.Vector4:
                case ValueFormat.Color:
                    return $"{Number(value.x)}, {Number(value.y)}, {Number(value.z)}, {Number(value.w)}";
                default:
                    return Number(value.x);
            }
        }

        /// <summary>The single number a graph plots for a value.</summary>
        public static float Scalar(Vector4 value, ValueFormat format)
        {
            switch (format)
            {
                case ValueFormat.LinearVelocity:
                case ValueFormat.AngularVelocity:
                case ValueFormat.Vector2:
                case ValueFormat.Vector3:
                    return ((Vector3)value).magnitude;
                case ValueFormat.AngularVelocity2D:
                    return value.z;
                default:
                    return value.x;
            }
        }

        #endregion
    }
}
