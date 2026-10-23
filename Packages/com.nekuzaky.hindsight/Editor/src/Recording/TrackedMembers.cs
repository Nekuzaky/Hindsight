using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal enum ValueFormat : byte
    {
        Float,
        Integer,
        Boolean,
        Enum,
        Vector2,
        Vector3,
        Vector4,
        Rotation,
        Color,
        LinearVelocity,
        AngularVelocity,
        AngularVelocity2D
    }

    internal sealed class TrackedMember
    {
        public TrackedMember(string key, string label, ValueFormat format, Type enumType, Func<Component, Vector4> getter)
        {
            Key = key;
            Label = label;
            Format = format;
            EnumType = enumType;
            Getter = getter;
        }

        public string Key { get; }
        public string Label { get; }
        public ValueFormat Format { get; }
        public Type EnumType { get; }
        public Func<Component, Vector4> Getter { get; }
    }

    internal static class TrackedMembers
    {
        #region Public

        public static bool HasAny
        {
            get
            {
                EnsureIndex();
                return _attributed.Count > 0 || HindsightSettings.instance.PinCount > 0;
            }
        }

        #endregion


        #region Private and Protected

        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;
        private const int MaxInheritanceDepth = 32;
        private const int MaxPathDepth = 8;

        private static readonly TrackedMember[] None = Array.Empty<TrackedMember>();
        private static readonly Dictionary<Type, TrackedMember[]> Cache = new();
        private static readonly Dictionary<Type, List<FieldInfo>> _attributed = new();
        private static bool _indexed;

        #endregion


        #region Main API

        public static TrackedMember[] For(Type type)
        {
            Debug.Assert(type != null, "A component type is required.");
            if (Cache.TryGetValue(type, out var members))
                return members;

            members = Build(type);
            Cache[type] = members;
            return members;
        }

        public static void Invalidate()
        {
            Cache.Clear();
            _attributed.Clear();
            _indexed = false;
        }

        public static bool IsSupported(Type type) => TryGetFormat(type, out _);

        public static bool TryResolvePath(Type type, string path, List<FieldInfo> chain)
        {
            chain.Clear();
            if (string.IsNullOrEmpty(path) || path.Contains("Array.data"))
                return false;

            var names = path.Split('.');
            if (names.Length > MaxPathDepth)
                return false;

            var current = type;
            for (var i = 0; i < names.Length; i++)
            {
                var field = FindField(current, names[i]);
                if (field == null)
                    return false;
                chain.Add(field);
                current = field.FieldType;
            }
            return TryGetFormat(current, out _);
        }

        #endregion


        #region Tools and Utilities

        private static void EnsureIndex()
        {
            if (_indexed)
                return;

            foreach (var field in TypeCache.GetFieldsWithAttribute<HindsightTrackAttribute>())
            {
                var owner = field.DeclaringType;
                if (owner == null || field.IsStatic || !typeof(Component).IsAssignableFrom(owner))
                    continue;
                if (!_attributed.TryGetValue(owner, out var list))
                    _attributed[owner] = list = new List<FieldInfo>();
                list.Add(field);
            }
            _indexed = true;
        }

        private static TrackedMember[] Build(Type type)
        {
            EnsureIndex();
            var result = new List<TrackedMember>();
            var current = type;
            for (var depth = 0; depth < MaxInheritanceDepth && current != null && current != typeof(Component); depth++)
            {
                if (_attributed.TryGetValue(current, out var fields))
                {
                    foreach (var field in fields)
                        TryAdd(result, type, new List<FieldInfo> { field }, LabelOf(field));
                }
                current = current.BaseType;
            }

            AddPins(type, result);
            return result.Count == 0 ? None : result.ToArray();
        }

        private static void AddPins(Type type, List<TrackedMember> result)
        {
            var settings = HindsightSettings.instance;
            for (var i = 0; i < settings.PinCount; i++)
            {
                var pin = settings.GetPin(i);
                if (!IsSameOrBase(type, pin.TypeName))
                    continue;

                var chain = new List<FieldInfo>();
                if (TryResolvePath(type, pin.Path, chain))
                    TryAdd(result, type, chain, ObjectNames.NicifyVariableName(chain[chain.Count - 1].Name));
            }
        }

        private static void TryAdd(List<TrackedMember> result, Type owner, List<FieldInfo> chain, string label)
        {
            var key = KeyOf(owner, chain);
            foreach (var existing in result)
            {
                if (existing.Key == key)
                    return;
            }

            var leaf = chain[chain.Count - 1].FieldType;
            if (!TryGetFormat(leaf, out var format))
            {
                Debug.LogWarning($"[Hindsight] {owner.Name}.{chain[chain.Count - 1].Name} has an unsupported type ({leaf.Name}) and is not recorded.");
                return;
            }

            var getter = Compile(owner, chain, format);
            result.Add(new TrackedMember(key, label, format, leaf.IsEnum ? leaf : null, getter));
        }

        private static Func<Component, Vector4> Compile(Type owner, List<FieldInfo> chain, ValueFormat format)
        {
            var component = Expression.Parameter(typeof(Component), "component");
            Expression access = Expression.Convert(component, owner);
            foreach (var field in chain)
                access = Expression.Field(access, field);

            var body = ToVector4(access, format);
            return Expression.Lambda<Func<Component, Vector4>>(body, component).Compile();
        }

        private static Expression ToVector4(Expression value, ValueFormat format)
        {
            var ctor = typeof(Vector4).GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            Debug.Assert(ctor != null, "Vector4 must expose a four-float constructor.");
            var zero = Expression.Constant(0f);

            Expression AsFloat(Expression e) => Expression.Convert(e, typeof(float));
            Expression Member(string name) => Expression.PropertyOrField(value, name);

            switch (format)
            {
                case ValueFormat.Boolean:
                    return Expression.New(ctor, Expression.Condition(value, Expression.Constant(1f), zero), zero, zero, zero);
                case ValueFormat.Enum:
                    return Expression.New(ctor, AsFloat(Expression.Convert(value, typeof(long))), zero, zero, zero);
                case ValueFormat.Vector2:
                    return Expression.New(ctor, AsFloat(Member("x")), AsFloat(Member("y")), zero, zero);
                case ValueFormat.Vector3:
                    return Expression.New(ctor, AsFloat(Member("x")), AsFloat(Member("y")), AsFloat(Member("z")), zero);
                case ValueFormat.Vector4:
                case ValueFormat.Rotation:
                    return Expression.New(ctor, AsFloat(Member("x")), AsFloat(Member("y")), AsFloat(Member("z")), AsFloat(Member("w")));
                case ValueFormat.Color:
                    return Expression.New(ctor, Member("r"), Member("g"), Member("b"), Member("a"));
                default:
                    return Expression.New(ctor, AsFloat(value), zero, zero, zero);
            }
        }

        private static bool TryGetFormat(Type type, out ValueFormat format)
        {
            if (type == typeof(float) || type == typeof(double))
                format = ValueFormat.Float;
            else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                     || type == typeof(uint) || type == typeof(ushort) || type == typeof(sbyte))
                format = ValueFormat.Integer;
            else if (type == typeof(bool))
                format = ValueFormat.Boolean;
            else if (type.IsEnum)
                format = ValueFormat.Enum;
            else if (type == typeof(Vector2) || type == typeof(Vector2Int))
                format = ValueFormat.Vector2;
            else if (type == typeof(Vector3) || type == typeof(Vector3Int))
                format = ValueFormat.Vector3;
            else if (type == typeof(Vector4))
                format = ValueFormat.Vector4;
            else if (type == typeof(Quaternion))
                format = ValueFormat.Rotation;
            else if (type == typeof(Color))
                format = ValueFormat.Color;
            else
            {
                format = default;
                return false;
            }
            return true;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            var current = type;
            for (var depth = 0; depth < MaxInheritanceDepth && current != null; depth++)
            {
                var field = current.GetField(name, InstanceFields);
                if (field != null)
                    return field;
                current = current.BaseType;
            }
            return null;
        }

        private static bool IsSameOrBase(Type type, string typeName)
        {
            var current = type;
            for (var depth = 0; depth < MaxInheritanceDepth && current != null; depth++)
            {
                if (current.FullName == typeName)
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        private static string LabelOf(FieldInfo field)
        {
            var attribute = field.GetCustomAttribute<HindsightTrackAttribute>();
            if (attribute != null && !string.IsNullOrEmpty(attribute.Label))
                return attribute.Label;

            var name = field.Name;
            if (name.StartsWith("<", StringComparison.Ordinal) && name.Contains(">k__BackingField"))
                name = name.Substring(1, name.IndexOf('>') - 1);
            return ObjectNames.NicifyVariableName(name);
        }

        private static string KeyOf(Type owner, List<FieldInfo> chain)
        {
            var path = chain[0].Name;
            for (var i = 1; i < chain.Count; i++)
                path += "." + chain[i].Name;
            return owner.FullName + "/" + path;
        }

        #endregion
    }
}
