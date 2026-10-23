using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// The <c>.hindsight</c> file: a header, then the recorder's history, events, objects and values, exactly as
    /// they were in memory. Saving writes a temporary file first, so a failed save never damages an existing one.
    /// </summary>
    internal static class RecordingFile
    {
        #region Public

        public const string Extension = "hindsight";

        public readonly struct Header
        {
            public Header(string unityVersion, string product, string savedAt)
            {
                UnityVersion = unityVersion;
                Product = product;
                SavedAt = savedAt;
            }

            public string UnityVersion { get; }
            public string Product { get; }
            public string SavedAt { get; }
        }

        /// <summary>A file name for a recording saved now, such as <c>Recording 2026-10-23 14-05-12.hindsight</c>.</summary>
        public static string DefaultName => $"Recording {DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)}.{Extension}";

        public static void Save(Recorder recorder, string path)
        {
            Debug.Assert(recorder != null && !string.IsNullOrEmpty(path), "Saving needs a recorder and a path.");
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, NativeIO.BufferBytes))
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                {
                    writer.Write(Magic);
                    writer.Write(FormatVersion);
                    writer.Write(Application.unityVersion);
                    writer.Write(Application.productName);
                    writer.Write(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    recorder.WriteTo(writer, new byte[NativeIO.BufferBytes]);
                }
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        /// <summary>Reads a recording. Throws <see cref="InvalidDataException"/> when the file is not a valid one.</summary>
        public static Recorder Load(string path, out Header header)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, NativeIO.BufferBytes);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (stream.Length < 8 || reader.ReadUInt32() != Magic)
                throw new InvalidDataException("This file is not a Hindsight recording.");
            var version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"This recording uses format {version}; this version of Hindsight reads format {FormatVersion}. Update Hindsight to open it.");

            header = new Header(reader.ReadString(), reader.ReadString(), reader.ReadString());
            return Recorder.ReadFrom(reader, new byte[NativeIO.BufferBytes]);
        }

        #endregion


        #region Private and Protected

        /// <summary>"HSGT" read as a little-endian integer.</summary>
        private const uint Magic = 0x54475348;

        private const int FormatVersion = 1;

        #endregion
    }
}
