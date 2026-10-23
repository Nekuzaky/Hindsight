using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Editor
{
    // Saving and opening: a recorder read from a file holds the same history, events, slots and streams as the one
    // that wrote it. Its slots point to the objects of the open scenes found at the same place in the hierarchy;
    // objects created while playing have no counterpart and are left out of rewinds.
    internal sealed partial class Recorder
    {
        #region Public

        /// <summary>True for a recording opened from a file: it is only viewed, never recorded into.</summary>
        public bool IsFromFile { get; private set; }

        /// <summary>Objects of a file found in the open scenes, and objects that were not.</summary>
        public int MatchedObjects { get; private set; }

        public int MissingObjects { get; private set; }

        public void WriteTo(BinaryWriter writer, byte[] buffer)
        {
            Commit();
            writer.Write(Options.HistorySeconds);
            writer.Write((long)History.Transforms.Count);
            writer.Write((long)History.Values.Count);
            writer.Write(History.CheckpointCount);
            writer.Write(SlotCount);
            writer.Write(StreamCount);
            History.WriteTo(writer, buffer);
            Markers.WriteTo(writer);
            WriteSlots(writer, buffer);
            WriteStreams(writer, buffer);
        }

        public static Recorder ReadFrom(BinaryReader reader, byte[] buffer)
        {
            var seconds = reader.ReadDouble();
            var words = reader.ReadInt64();
            var values = reader.ReadInt64();
            var checkpoints = reader.ReadInt32();
            var slots = NativeIO.CheckCount(reader.ReadInt32(), TransformCodec.MaxSlots, "objects");
            var streams = NativeIO.CheckCount(reader.ReadInt32(), int.MaxValue / 64, "values");

            var recorder = new Recorder(RecorderOptions.ForFile(seconds, words, values, checkpoints, slots));
            try
            {
                recorder.IsFromFile = true;
                recorder.History.ReadFrom(reader, buffer, slots, streams);
                recorder.Markers.ReadFrom(reader);
                recorder.ReadSlots(reader, buffer, slots);
                recorder.ReadStreams(reader, buffer, streams);
                return recorder;
            }
            catch
            {
                recorder.Dispose();
                throw;
            }
        }

        #endregion


        #region Tools and Utilities

        private void WriteSlots(BinaryWriter writer, byte[] buffer)
        {
            var chain = new List<Transform>(16);
            for (var slot = 0; slot < SlotCount; slot++)
            {
                writer.Write(_birth[slot]);
                writer.Write(_death[slot]);
                writer.Write(_isRoot[slot]);
                writer.Write(_bodyLinear[slot]);
                writer.Write(_bodyAngular[slot]);
                writer.Write(_firstField[slot]);
                ScenePath.Write(writer, _death[slot] == Alive ? _transforms[slot] : null, chain);
            }
            NativeIO.Write(writer, _last, 0, SlotCount, buffer);
        }

        private void ReadSlots(BinaryReader reader, byte[] buffer, int slots)
        {
            if (slots > _slotCapacity)
                ResizeSlots(Mathf.NextPowerOfTwo(slots));
            SlotCount = slots;

            // Slots without an object in the open scenes point to a transform destroyed right after: the jobs see
            // them as invalid and leave them alone.
            var placeholder = new GameObject("Hindsight Placeholder") { hideFlags = HideFlags.HideAndDontSave };
            var roots = new List<GameObject>(64);
            var taken = new HashSet<EntityId>();
            try
            {
                for (var slot = 0; slot < slots; slot++)
                {
                    var transform = ReadSlot(reader, slot, roots);
                    if (transform != null && !taken.Add(transform.GetEntityId()))
                        transform = null;
                    BindSlot(slot, transform);
                    _access.Add(transform != null ? transform : placeholder.transform);
                }
            }
            finally
            {
                Object.DestroyImmediate(placeholder);
            }
            NativeIO.Read(reader, _last, 0, slots, buffer);
        }

        private Transform ReadSlot(BinaryReader reader, int slot, List<GameObject> roots)
        {
            _birth[slot] = reader.ReadInt32();
            _death[slot] = reader.ReadInt32();
            _isRoot[slot] = reader.ReadBoolean();
            _bodyLinear[slot] = reader.ReadInt32();
            _bodyAngular[slot] = reader.ReadInt32();
            _firstField[slot] = reader.ReadInt32();
            return ScenePath.Read(reader, roots);
        }

        private void BindSlot(int slot, Transform transform)
        {
            var alive = _death[slot] == Alive;
            _transforms[slot] = transform;
            _alive[slot] = alive ? (byte)1 : (byte)0;
            if (!alive)
                return;
            AliveCount++;
            if (transform == null)
            {
                MissingObjects++;
                return;
            }
            MatchedObjects++;
            _slotById[transform.GetEntityId()] = slot;
        }

        private void WriteStreams(BinaryWriter writer, byte[] buffer)
        {
            for (var stream = 0; stream < StreamCount; stream++)
            {
                writer.Write((byte)_streamKind[stream]);
                writer.Write(_streamOwner[stream]);
                writer.Write(_streamBirth[stream]);
                writer.Write(_streamDeath[stream]);
                writer.Write((byte)_streamFormat[stream]);
                writer.Write(_streamLabel[stream] ?? string.Empty);
                writer.Write(_streamEnum[stream]?.AssemblyQualifiedName ?? string.Empty);
                writer.Write(_nextField[stream]);
            }
            NativeIO.Write(writer, _values, 0, StreamCount, buffer);
        }

        private void ReadStreams(BinaryReader reader, byte[] buffer, int streams)
        {
            if (streams > _streamCapacity)
                AllocateStreams(Mathf.NextPowerOfTwo(streams));
            StreamCount = streams;
            for (var stream = 0; stream < streams; stream++)
            {
                _streamKind[stream] = (StreamKind)reader.ReadByte();
                _streamOwner[stream] = reader.ReadInt32();
                _streamBirth[stream] = reader.ReadInt32();
                _streamDeath[stream] = reader.ReadInt32();
                _streamFormat[stream] = (ValueFormat)reader.ReadByte();
                _streamLabel[stream] = reader.ReadString();
                var enumName = reader.ReadString();
                var enumType = enumName.Length == 0 ? null : Type.GetType(enumName, false);
                _streamEnum[stream] = enumType != null && enumType.IsEnum ? enumType : null;
                _nextField[stream] = reader.ReadInt32();
            }
            NativeIO.Read(reader, _values, 0, streams, buffer);
            CheckLinks();
        }

        /// <summary>Every slot and stream index read from the file must point inside the recording.</summary>
        private void CheckLinks()
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (_bodyLinear[slot] >= StreamCount || _bodyAngular[slot] >= StreamCount || _firstField[slot] >= StreamCount)
                    throw new InvalidDataException("The recording is damaged: a value points outside the recording.");
            }
            for (var stream = 0; stream < StreamCount; stream++)
            {
                if (_nextField[stream] >= StreamCount || _streamOwner[stream] >= SlotCount)
                    throw new InvalidDataException("The recording is damaged: a value points outside the recording.");
            }
        }

        #endregion
    }
}
