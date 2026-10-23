using Unity.Collections;
using Unity.Mathematics;

namespace Nekuzaky.Hindsight.Editor
{
    internal static class TransformCodec
    {
        #region Public

        public const int ComponentCount = 10;
        public const int FullMask = (1 << ComponentCount) - 1;
        public const int HeaderWords = 2;
        public const int EntryWords = 2;
        public const int MaxWordsPerSlot = EntryWords + ComponentCount;

        public const int MaxPayloadOffset = (1 << (32 - ComponentCount)) - 1;

        public const int MaxSlots = MaxPayloadOffset / ComponentCount;

        public static long WorstCaseWords(int slotCount) => HeaderWords + (long)slotCount * MaxWordsPerSlot;

        #endregion


        #region Main API

        public static int Diff(in TransformSample current, in TransformSample last)
        {
            var mask = 0;
            for (var component = 0; component < ComponentCount; component++)
            {
                if (!(Get(current, component) == Get(last, component)))
                    mask |= 1 << component;
            }
            return mask;
        }

        public static int EntryCount(NativeArray<uint> ring, long block) => (int)ring[Index(ring, block)];

        public static int BlockWords(NativeArray<uint> ring, long block) => (int)ring[Index(ring, block + 1)];

        public static int SlotAt(NativeArray<uint> ring, long block, int entry)
        {
            return (int)ring[Index(ring, block + HeaderWords + (long)entry * EntryWords)];
        }

        public static void ApplyEntry(NativeArray<uint> ring, long block, int entry, ref TransformSample state)
        {
            var entries = EntryCount(ring, block);
            var meta = ring[Index(ring, block + HeaderWords + (long)entry * EntryWords + 1)];
            var mask = (int)(meta & FullMask);
            var at = block + HeaderWords + (long)entries * EntryWords + (meta >> ComponentCount);
            for (var component = 0; component < ComponentCount; component++)
            {
                if ((mask & (1 << component)) == 0)
                    continue;
                Set(ref state, component, math.asfloat(ring[Index(ring, at)]));
                at++;
            }
        }

        public static void ApplyBlock(NativeArray<uint> ring, long block, NativeArray<TransformSample> states, int slotCount)
        {
            var length = ring.Length;
            var entries = EntryCount(ring, block);
            var entry = Index(ring, block + HeaderWords);
            var payload = Index(ring, block + HeaderWords + (long)entries * EntryWords);
            for (var i = 0; i < entries; i++)
            {
                var slot = (int)ring[entry];
                var mask = (int)(ring[Next(entry, length)] & FullMask);
                entry = Next(Next(entry, length), length);
                if (slot >= slotCount)
                {
                    payload = (payload + math.countbits(mask)) % length;
                    continue;
                }

                var state = states[slot];
                for (var component = 0; component < ComponentCount; component++)
                {
                    if ((mask & (1 << component)) == 0)
                        continue;
                    Set(ref state, component, math.asfloat(ring[payload]));
                    payload = Next(payload, length);
                }
                states[slot] = state;
            }
        }

        private static int Next(int index, int length) => index + 1 == length ? 0 : index + 1;

        public static float Get(in TransformSample sample, int component)
        {
            switch (component)
            {
                case 0: return sample.Position.x;
                case 1: return sample.Position.y;
                case 2: return sample.Position.z;
                case 3: return sample.Rotation.x;
                case 4: return sample.Rotation.y;
                case 5: return sample.Rotation.z;
                case 6: return sample.Rotation.w;
                case 7: return sample.Scale.x;
                case 8: return sample.Scale.y;
                default: return sample.Scale.z;
            }
        }

        public static void Set(ref TransformSample sample, int component, float value)
        {
            switch (component)
            {
                case 0: sample.Position.x = value; break;
                case 1: sample.Position.y = value; break;
                case 2: sample.Position.z = value; break;
                case 3: sample.Rotation.x = value; break;
                case 4: sample.Rotation.y = value; break;
                case 5: sample.Rotation.z = value; break;
                case 6: sample.Rotation.w = value; break;
                case 7: sample.Scale.x = value; break;
                case 8: sample.Scale.y = value; break;
                default: sample.Scale.z = value; break;
            }
        }

        public static int Index(NativeArray<uint> ring, long absolute) => (int)(absolute % ring.Length);

        #endregion
    }

    internal struct TransformBlockWriter
    {
        #region Public

        public TransformBlockWriter(NativeArray<uint> ring, long head, int entries)
        {
            _ring = ring;
            _head = head;
            _entries = entries;
            _written = 0;
            _payload = 0;
        }

        #endregion


        #region Private and Protected

        private NativeArray<uint> _ring;
        private readonly long _head;
        private readonly int _entries;
        private int _written;
        private int _payload;

        #endregion


        #region Main API

        public void Add(int slot, in TransformSample value, in TransformSample last)
        {
            var mask = TransformCodec.Diff(value, last);
            var entry = _head + TransformCodec.HeaderWords + (long)_written * TransformCodec.EntryWords;
            _ring[TransformCodec.Index(_ring, entry)] = (uint)slot;
            _ring[TransformCodec.Index(_ring, entry + 1)] = (uint)mask | ((uint)_payload << TransformCodec.ComponentCount);

            var at = _head + TransformCodec.HeaderWords + (long)_entries * TransformCodec.EntryWords + _payload;
            for (var component = 0; component < TransformCodec.ComponentCount; component++)
            {
                if ((mask & (1 << component)) == 0)
                    continue;
                _ring[TransformCodec.Index(_ring, at)] = math.asuint(TransformCodec.Get(value, component));
                at++;
                _payload++;
            }
            _written++;
        }

        public int Finish()
        {
            if (_entries == 0)
                return 0;
            var words = TransformCodec.HeaderWords + _entries * TransformCodec.EntryWords + _payload;
            _ring[TransformCodec.Index(_ring, _head)] = (uint)_entries;
            _ring[TransformCodec.Index(_ring, _head + 1)] = (uint)words;
            return words;
        }

        #endregion
    }
}
