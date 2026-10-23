using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Nekuzaky.Hindsight.Editor
{
    internal static class NativeIO
    {
        #region Public

        public const int BufferBytes = 1 << 16;

        public static void Write<T>(BinaryWriter writer, NativeArray<T> array, int start, int count, byte[] buffer) where T : unmanaged
        {
            var size = Marshal.SizeOf<T>();
            var perChunk = Math.Max(1, buffer.Length / size);
            for (var done = 0; done < count; done += perChunk)
            {
                var length = Math.Min(perChunk, count - done);
                var bytes = array.GetSubArray(start + done, length).Reinterpret<byte>(size);
                NativeArray<byte>.Copy(bytes, 0, buffer, 0, bytes.Length);
                writer.Write(buffer, 0, bytes.Length);
            }
        }

        public static void Read<T>(BinaryReader reader, NativeArray<T> array, int start, int count, byte[] buffer) where T : unmanaged
        {
            var size = Marshal.SizeOf<T>();
            var perChunk = Math.Max(1, buffer.Length / size);
            for (var done = 0; done < count; done += perChunk)
            {
                var length = Math.Min(perChunk, count - done);
                var bytes = array.GetSubArray(start + done, length).Reinterpret<byte>(size);
                ReadExactly(reader, buffer, bytes.Length);
                NativeArray<byte>.Copy(buffer, 0, bytes, 0, bytes.Length);
            }
        }

        public static int CheckCount(long count, long limit, string what)
        {
            if (count < 0 || count > limit)
                throw new InvalidDataException($"The recording is damaged: {count} {what}, at most {limit} expected.");
            return (int)count;
        }

        #endregion


        #region Tools and Utilities

        private static void ReadExactly(BinaryReader reader, byte[] buffer, int count)
        {
            var read = 0;
            for (var pass = 0; read < count && pass < count; pass++)
            {
                var got = reader.Read(buffer, read, count - read);
                if (got <= 0)
                    throw new EndOfStreamException("The recording ends too early: the file is incomplete.");
                read += got;
            }
        }

        #endregion
    }
}
