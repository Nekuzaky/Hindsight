using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Nekuzaky.Hindsight.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Dev
{
    [InitializeOnLoad]
    internal static class MediaCapture
    {
        #region Private and Protected

        private const string StageKey = "Hindsight.Media.Stage";
        private const string OutputKey = "Hindsight.Media.Output";
        private const double CaptureInterval = 1d / 15d;
        private const int ScrubBefore = 30;
        private const int ScrubAfter = 3;
        private const int HoldFrames = 18;

        private static readonly Rect SceneRect = new(40f, 60f, 1280f, 500f);
        private static readonly Rect WindowRect = new(40f, 590f, 1280f, 680f);
        private static readonly Rect PreferencesRect = new(40f, 60f, 1180f, 1180f);
        private const float FloorTop = -3.5f;
        private const int SlowMotionRepeats = 3;
        private static readonly Regex CometNumber = new(@"Comet #(\d+)");

        private enum Stage { None, Live, Rewind, Hold, Preferences, Done }

        private static Stage _stage;
        private static double _nextCapture;
        private static double _stageStart;
        private static int _frameIndex;
        private static int _target;
        private static int _shown;
        private static int _held;
        private static int _birth;
        private static int _repeats;
        private static bool _savedRewind;
        private static Transform _comet;
        private static string _output;

        #endregion


        #region Main API

        static MediaCapture()
        {
            _stage = (Stage)SessionState.GetInt(StageKey, 0);
            _output = SessionState.GetString(OutputKey, string.Empty);
            if (_stage == Stage.None)
                return;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Run()
        {
            _output = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Output", "media");
            var frames = Path.Combine(_output, "frames");
            if (Directory.Exists(frames))
                Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);
            SessionState.SetString(OutputKey, _output);
            foreach (var old in Directory.GetFiles(DocsImages, "screenshot-*.png"))
                File.Delete(old);

            var settings = HindsightSettings.instance;
            settings.RecordInPlayMode = true;
            settings.ShowMotionTrail = true;
            settings.ShowDestroyedGhosts = true;

            EditorSceneManager.OpenScene(DemoSceneBuilder.ScenePath, OpenSceneMode.Single);
            CloseCaptureWindows();
            var scene = ScriptableObject.CreateInstance<SceneView>();
            scene.Show();
            scene.position = SceneRect;
            scene.in2DMode = true;
            scene.LookAtDirect(new Vector3(0f, -1.3f, 0f), Quaternion.identity, 6.9f);

            var window = ScriptableObject.CreateInstance<HindsightWindow>();
            window.Show();
            window.position = WindowRect;

            SetStage(Stage.Live);
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.EnterPlaymode();
        }

        #endregion


        #region Stages

        private static void Update()
        {
            switch (_stage)
            {
                case Stage.Live: LiveStage(); break;
                case Stage.Rewind: RewindStage(); break;
                case Stage.Hold: HoldStage(); break;
                case Stage.Preferences: PreferencesStage(); break;
            }
        }

        private static void LiveStage()
        {
            if (!EditorApplication.isPlaying || HindsightSession.Recorder == null || Time.time < 1.2f)
                return;

            if (Time.time < 6f)
            {
                if (Due())
                    CaptureFrame();
                if (Time.time > 4.6f && !File.Exists(Path.Combine(DocsImages, "screenshot-recording.png")))
                    SaveComposite(Path.Combine(DocsImages, "screenshot-recording.png"));
                return;
            }

            var recorder = HindsightSession.Recorder;
            for (var i = 0; i < recorder.Markers.Count; i++)
            {
                ref readonly var marker = ref recorder.Markers[i];
                var match = CometNumber.Match(marker.Message);
                if (!marker.IsError || !match.Success || !recorder.History.Contains(marker.Sequence))
                    continue;

                var comet = GameObject.Find($"Hindsight Demo/Comet {match.Groups[1].Value}");
                if (comet == null)
                    continue;

                EditorApplication.isPaused = true;
                Selection.activeGameObject = comet;
                _target = marker.Sequence;
                recorder.TryGetSlot(comet.transform, out var slot);
                _birth = slot >= 0 ? recorder.BirthOf(slot) : _target;
                _comet = comet.transform;
                _shown = Math.Max(recorder.History.OldestSequence, _birth - ScrubBefore);
                HindsightSession.ShowFrame(_shown);
                SetStage(Stage.Rewind);
                return;
            }
        }

        private static void RewindStage()
        {
            if (!Due())
                return;

            CaptureFrame();
            var cometVisible = _shown >= _birth && _shown <= _target;
            if (cometVisible && ++_repeats < SlowMotionRepeats)
                return;
            _repeats = 0;

            if (!_savedRewind && _comet != null && _shown >= _birth && _comet.position.y < FloorTop)
            {
                SaveComposite(Path.Combine(DocsImages, "screenshot-rewind.png"));
                _savedRewind = true;
            }
            if (_shown >= _target + ScrubAfter)
            {
                SetStage(Stage.Hold);
                return;
            }
            _shown++;
            HindsightSession.ShowFrame(_shown);
        }

        private static void HoldStage()
        {
            if (!Due())
                return;
            CaptureFrame();
            if (++_held < HoldFrames)
                return;

            SaveWindow(FindWindow<HindsightWindow>(), Path.Combine(DocsImages, "screenshot-window.png"));
            SetStage(Stage.Preferences);
            EditorApplication.ExitPlaymode();
        }

        private static void PreferencesStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (_stageStart <= 0d)
            {
                CloseCaptureWindows();
                _stageStart = EditorApplication.timeSinceStartup;
                SettingsService.OpenUserPreferences(HindsightPreferences.Path);
                return;
            }
            if (EditorApplication.timeSinceStartup - _stageStart < 2.5d)
                return;

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (!window.GetType().Name.Contains("Preference"))
                    continue;
                if (window.position != PreferencesRect)
                {
                    window.position = PreferencesRect;
                    window.Focus();
                    _stageStart = EditorApplication.timeSinceStartup - 1.5d;
                    return;
                }
                SaveWindow(window, Path.Combine(DocsImages, "screenshot-preferences.png"));
            }
            SetStage(Stage.Done);
            SessionState.EraseInt(StageKey);
            EditorApplication.Exit(0);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && _stage == Stage.Preferences)
                _stageStart = 0d;
        }

        #endregion


        #region Capture

        private static string DocsImages
        {
            get
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), "docs", "images");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        private static bool Due()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < _nextCapture)
                return false;
            _nextCapture = now + CaptureInterval;
            return true;
        }

        private static void CaptureFrame()
        {
            var path = Path.Combine(_output, "frames", $"frame_{_frameIndex++:0000}.png");
            SaveComposite(path);
        }

        private static void SaveComposite(string path)
        {
            var scene = FindScene();
            var window = FindWindow<HindsightWindow>();
            if (scene == null || window == null)
                return;

            var top = Grab(scene);
            var bottom = Grab(window);
            if (top == null || bottom == null)
                return;

            var width = Math.Min(top.width, bottom.width);
            var composite = new Texture2D(width, top.height + bottom.height, TextureFormat.RGB24, false);
            composite.SetPixels(0, 0, width, bottom.height, bottom.GetPixels(0, 0, width, bottom.height));
            composite.SetPixels(0, bottom.height, width, top.height, top.GetPixels(0, 0, width, top.height));
            composite.Apply();
            File.WriteAllBytes(path, composite.EncodeToPNG());
            Object.DestroyImmediate(composite);
            Object.DestroyImmediate(top);
            Object.DestroyImmediate(bottom);
            scene.Repaint();
            window.Repaint();
        }

        private static void SaveWindow(EditorWindow window, string path)
        {
            if (window == null)
                return;
            var texture = Grab(window);
            if (texture == null)
                return;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static Texture2D Grab(EditorWindow window)
        {
            var rect = window.position;
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.RoundToInt(rect.width * scale);
            var height = Mathf.RoundToInt(rect.height * scale);
            var pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position * scale, width, height);
            if (pixels == null || pixels.Length != width * height)
                return null;

            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static SceneView FindScene()
        {
            foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>())
            {
                if (Mathf.Abs(view.position.x - SceneRect.x) < 2f && Mathf.Abs(view.position.y - SceneRect.y) < 40f)
                    return view;
            }
            return null;
        }

        private static void CloseCaptureWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HindsightWindow>())
                window.Close();
            foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>())
            {
                if (Mathf.Abs(view.position.x - SceneRect.x) < 2f && Mathf.Abs(view.position.y - SceneRect.y) < 40f)
                    view.Close();
            }
        }

        private static T FindWindow<T>() where T : EditorWindow
        {
            var windows = Resources.FindObjectsOfTypeAll<T>();
            return windows.Length > 0 ? windows[0] : null;
        }

        private static void SetStage(Stage stage)
        {
            _stage = stage;
            _stageStart = 0d;
            _held = 0;
            SessionState.SetInt(StageKey, (int)stage);
        }

        #endregion
    }
}
