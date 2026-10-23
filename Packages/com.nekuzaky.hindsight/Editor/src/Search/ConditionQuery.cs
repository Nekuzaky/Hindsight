using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum Comparison : byte
    {
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
        Equal,
        NotEqual
    }

    internal readonly struct ConditionQuery
    {
        #region Public

        public ConditionQuery(bool anyObject, string channel, Comparison comparison, float value)
        {
            AnyObject = anyObject;
            Channel = channel;
            Comparison = comparison;
            Value = value;
        }

        public bool AnyObject { get; }
        public string Channel { get; }
        public Comparison Comparison { get; }
        public float Value { get; }

        public bool Matches(float value)
        {
            if (float.IsNaN(value))
                return false;
            switch (Comparison)
            {
                case Comparison.Less: return value < Value;
                case Comparison.LessOrEqual: return value <= Value;
                case Comparison.Greater: return value > Value;
                case Comparison.GreaterOrEqual: return value >= Value;
                case Comparison.Equal: return Math.Abs(value - Value) <= Tolerance;
                default: return Math.Abs(value - Value) > Tolerance;
            }
        }

        public static bool TryParse(string text, out ConditionQuery query, out string error)
        {
            query = default;
            var match = Pattern.Match(text ?? string.Empty);
            if (!match.Success)
            {
                error = "Write a condition such as  y < -5,  speed > 20,  Health <= 0  or  any y < -10.";
                return false;
            }

            if (!TryParseValue(match.Groups["value"].Value, out var value))
            {
                error = $"'{match.Groups["value"].Value}' is not a number.";
                return false;
            }

            query = new ConditionQuery(match.Groups["any"].Success, match.Groups["channel"].Value.Trim(),
                ParseComparison(match.Groups["op"].Value), value);
            error = null;
            return true;
        }

        public override string ToString()
        {
            var op = Comparison switch
            {
                Comparison.Less => "<",
                Comparison.LessOrEqual => "<=",
                Comparison.Greater => ">",
                Comparison.GreaterOrEqual => ">=",
                Comparison.Equal => "==",
                _ => "!="
            };
            return $"{(AnyObject ? "any " : string.Empty)}{Channel} {op} {Value.ToString("0.###", CultureInfo.InvariantCulture)}";
        }

        #endregion


        #region Private and Protected

        private const float Tolerance = 1e-4f;

        private static readonly Regex Pattern = new(
            @"^\s*(?<any>any\s+)?(?<channel>[A-Za-z_][\w .]*?)\s*(?<op><=|>=|==|!=|<|>|=)\s*(?<value>\S+)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool TryParseValue(string text, out float value)
        {
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = 1f;
                return true;
            }
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                value = 0f;
                return true;
            }
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static Comparison ParseComparison(string op)
        {
            switch (op)
            {
                case "<": return Comparison.Less;
                case "<=": return Comparison.LessOrEqual;
                case ">": return Comparison.Greater;
                case ">=": return Comparison.GreaterOrEqual;
                case "!=": return Comparison.NotEqual;
                default: return Comparison.Equal;
            }
        }

        #endregion
    }
}
