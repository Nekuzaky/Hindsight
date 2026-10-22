using System;
using System.Diagnostics;

namespace Nekuzaky.Hindsight
{
    /// <summary>
    /// Records a field of a MonoBehaviour on every frame so you can read its value at any point of the timeline
    /// and plot it in the Hindsight window. Supports float, double, int, long, short, byte, bool, enums, Vector2,
    /// Vector3, Vector4, Vector2Int, Vector3Int, Quaternion and Color.
    /// For an auto-property, put the attribute on its backing field: <c>[field: HindsightTrack]</c>.
    /// The attribute is removed from player builds.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public sealed class HindsightTrackAttribute : Attribute
    {
        #region Public

        /// <param name="label">Name shown in the Hindsight window. Defaults to the field name.</param>
        public HindsightTrackAttribute(string label = null)
        {
            Label = label;
        }

        public string Label { get; }

        #endregion
    }
}
