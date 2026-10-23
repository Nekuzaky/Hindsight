using System.IO;
using UnityEditor;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    internal static class RecordingCommands
    {
        #region Public

        public static bool CanSave()
        {
            var recorder = HindsightSession.Recorder;
            return recorder != null && !recorder.IsFromFile && !recorder.History.IsEmpty;
        }

        public static bool CanOpen() => !EditorApplication.isPlaying;

        public static void SaveWithDialog()
        {
            if (!CanSave())
                return;
            var path = EditorUtility.SaveFilePanel("Save Hindsight Recording", LastFolder, RecordingFile.DefaultName, RecordingFile.Extension);
            if (string.IsNullOrEmpty(path))
                return;

            LastFolder = Path.GetDirectoryName(path);
            var error = HindsightSession.Save(path);
            if (error != null)
                EditorUtility.DisplayDialog("Hindsight", $"The recording could not be saved.\n\n{error}", "OK");
            else
                Debug.Log($"{HindsightSession.LogPrefix} Recording saved to {path}");
        }

        public static void OpenWithDialog()
        {
            if (!CanOpen())
                return;
            var path = EditorUtility.OpenFilePanel("Open Hindsight Recording", LastFolder, RecordingFile.Extension);
            if (string.IsNullOrEmpty(path))
                return;

            LastFolder = Path.GetDirectoryName(path);
            var error = HindsightSession.Open(path);
            if (error != null)
            {
                EditorUtility.DisplayDialog("Hindsight", $"The recording could not be opened.\n\n{error}", "OK");
                return;
            }

            HindsightWindow.Open();
            var recorder = HindsightSession.Recorder;
            if (recorder.MissingObjects > 0)
                Debug.Log($"{HindsightSession.LogPrefix} {recorder.MissingObjects} recorded objects are not in the open scenes (created while playing, renamed or moved since). Their events and values are in the window; the Scene view only moves the {recorder.MatchedObjects} objects it found.");
        }

        #endregion


        #region Private and Protected

        private const string FolderKey = "Hindsight.RecordingFolder";

        private static string LastFolder
        {
            get => EditorPrefs.GetString(FolderKey, Path.GetDirectoryName(Application.dataPath));
            set => EditorPrefs.SetString(FolderKey, value ?? string.Empty);
        }

        #endregion
    }
}
