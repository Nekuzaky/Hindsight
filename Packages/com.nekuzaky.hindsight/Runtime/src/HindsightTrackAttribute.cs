using System;
using System.Diagnostics;

namespace Nekuzaky.Hindsight
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public sealed class HindsightTrackAttribute : Attribute
    {
        #region Public

        public HindsightTrackAttribute(string label = null)
        {
            Label = label;
        }

        public string Label { get; }

        #endregion
    }
}
