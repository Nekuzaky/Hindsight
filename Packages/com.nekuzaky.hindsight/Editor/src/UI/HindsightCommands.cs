using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    [InitializeOnLoad]
    internal static class HindsightCommands
    {
        #region Private and Protected

        private static readonly List<FieldInfo> Chain = new();

        #endregion


        #region Main API

        static HindsightCommands()
        {
            EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
        }

        [Shortcut("Hindsight/Add Bookmark", KeyCode.F9)]
        public static void AddBookmark()
        {
            if (!HindsightSession.AddBookmark())
                Debug.Log($"{HindsightSession.LogPrefix} Bookmarks are added while recording in Play Mode.");
        }

        [Shortcut("Hindsight/Step Back")]
        public static void StepBack() => Navigation.StepBack();

        [Shortcut("Hindsight/Step Forward")]
        public static void StepForward() => Navigation.StepForward();

        [Shortcut("Hindsight/Previous Event")]
        public static void PreviousEvent() => Navigation.PreviousEvent(MarkerFilter.All);

        [Shortcut("Hindsight/Next Event")]
        public static void NextEvent() => Navigation.NextEvent(MarkerFilter.All);

        [Shortcut("Hindsight/Back to Live")]
        public static void Live() => Navigation.Live();

        [Shortcut("Hindsight/Resume From Here")]
        public static void ResumeFromHere() => HindsightSession.ResumeFromShownFrame();

        [MenuItem("Tools/Hindsight/Resume From Shown Frame", priority = 3)]
        public static void ResumeFromHereMenu() => HindsightSession.ResumeFromShownFrame();

        [MenuItem("Tools/Hindsight/Resume From Shown Frame", true)]
        public static bool CanResumeFromHere() => HindsightSession.CanResumeFromShownFrame;

        [MenuItem("Tools/Hindsight/Add Bookmark", priority = 2)]
        public static void AddBookmarkFromMenu() => AddBookmark();

        [MenuItem("Tools/Hindsight/Add Bookmark", true)]
        public static bool CanAddBookmark() => HindsightSession.IsRecording;

        [MenuItem("Tools/Hindsight/Save Recording...", priority = 20)]
        public static void SaveRecording() => RecordingCommands.SaveWithDialog();

        [MenuItem("Tools/Hindsight/Save Recording...", true)]
        public static bool CanSaveRecording() => RecordingCommands.CanSave();

        [MenuItem("Tools/Hindsight/Open Recording...", priority = 21)]
        public static void OpenRecording() => RecordingCommands.OpenWithDialog();

        [MenuItem("Tools/Hindsight/Open Recording...", true)]
        public static bool CanOpenRecording() => RecordingCommands.CanOpen();

        [MenuItem("Tools/Hindsight/Close Recording", priority = 22)]
        public static void CloseRecording() => HindsightSession.Close();

        [MenuItem("Tools/Hindsight/Close Recording", true)]
        public static bool CanCloseRecording() => HindsightSession.OpenedFile != null;

        [MenuItem("Tools/Hindsight/Documentation", priority = 40)]
        public static void OpenDocumentation() => Application.OpenURL(HindsightWindow.DocumentationUrl);

        [MenuItem("Tools/Hindsight/GitHub Repository", priority = 41)]
        public static void OpenRepository() => Application.OpenURL(HindsightWindow.RepositoryUrl);

        [MenuItem("Tools/Hindsight/Support the Project", priority = 42)]
        public static void OpenSupport() => Application.OpenURL(HindsightWindow.CoffeeUrl);

        #endregion


        #region Tools and Utilities

        private static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
        {
            if (property == null || property.serializedObject.targetObject is not MonoBehaviour behaviour)
                return;

            var type = behaviour.GetType();
            var path = property.propertyPath;
            if (!TrackedMembers.TryResolvePath(type, path, Chain))
                return;

            var settings = HindsightSettings.instance;
            var typeName = type.FullName;
            var pinned = settings.IsPinned(typeName, path);
            menu.AddItem(new GUIContent(pinned ? "Stop Tracking in Hindsight" : "Track in Hindsight"), pinned, () =>
            {
                if (pinned)
                    settings.RemovePin(typeName, path);
                else if (settings.AddPin(typeName, path) && EditorApplication.isPlaying)
                    Debug.Log($"{HindsightSession.LogPrefix} {type.Name}.{path} is tracked on objects that appear from now on, and on every object from the next Play Mode.");
            });
        }

        #endregion
    }
}
