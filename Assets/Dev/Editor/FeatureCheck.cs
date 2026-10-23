using System.Collections.Generic;
using System.IO;
using System.Text;
using Nekuzaky.Hindsight.Editor;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nekuzaky.Hindsight.Dev
{
    [InitializeOnLoad]
    internal static class FeatureCheck
    {
        #region Private and Protected

        private const string ActiveKey = "Hindsight.FeatureCheck.Active";
        private const string ReportKey = "Hindsight.FeatureCheck.Report";
        private const string ScenePath = "Assets/Dev/FeatureCheck.unity";
        private const int RecordFrames = 120;
        private const int ResumeFrame = 50;
        private const int AfterResumeFrames = 10;

        private static int _frame;
        private static int _afterResume = -1;
        private static Vector3 _expectedVelocity;
        private static Vector2 _expectedVelocity2D;

        private static string Output => Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Output", "feature-check");
        private static string RecordingPath => Path.Combine(Output, "check." + RecordingFile.Extension);

        #endregion


        #region Main API

        static FeatureCheck()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (EditorApplication.isPlaying)
                Begin();
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(ReportKey, string.Empty);
            HindsightSettings.instance.RecordInPlayMode = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Frame Driver").AddComponent<FrameDriver>();
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball 3D";
            ball.transform.position = new Vector3(0f, 10f, 0f);
            ball.AddComponent<Rigidbody>();
            var disc = new GameObject("Disc 2D");
            disc.transform.position = new Vector3(5f, 10f, 0f);
            disc.AddComponent<CircleCollider2D>();
            disc.AddComponent<Rigidbody2D>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.EnterPlaymode();
        }

        #endregion


        #region Driving

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                Begin();
            else if (change == PlayModeStateChange.EnteredEditMode)
                CheckOpenedRecording();
        }

        private static void Begin()
        {
            _frame = 0;
            _afterResume = -1;
            FrameDriver.Tick = Step;
        }

        private static void Step()
        {
            _frame++;
            if (_frame == 1)
            {
                GameObject.Find("Ball 3D").GetComponent<Rigidbody>().linearVelocity = new Vector3(3f, 2f, 1f);
                GameObject.Find("Disc 2D").GetComponent<Rigidbody2D>().linearVelocity = new Vector2(-2f, 4f);
            }
            if (_afterResume >= 0)
            {
                if (++_afterResume == AfterResumeFrames)
                    FinishPlayMode();
                return;
            }
            if (_frame == RecordFrames)
                Resume();
        }

        private static void Resume()
        {
            var recorder = HindsightSession.Recorder;
            recorder.Commit();
            var target = recorder.History.NewestSequence - (RecordFrames - ResumeFrame);
            Check(HindsightSession.ShowFrame(target), "Rewinding to a past frame in Play Mode");

            var ball = GameObject.Find("Ball 3D").GetComponent<Rigidbody>();
            var disc = GameObject.Find("Disc 2D").GetComponent<Rigidbody2D>();
            _expectedVelocity = RecordedVelocity(recorder, ball.transform);
            _expectedVelocity2D = RecordedVelocity(recorder, disc.transform);
            var shownPosition = ball.transform.position;
            ball.linearVelocity = Vector3.zero;
            disc.linearVelocity = Vector2.zero;
            Check(!Near(ball.linearVelocity, _expectedVelocity) && !Near(disc.linearVelocity, _expectedVelocity2D), "The live velocities differ from the recorded ones before resuming");

            Check(HindsightSession.CanResumeFromShownFrame, "Resume here is offered while rewinding");
            Check(HindsightSession.ResumeFromShownFrame(), "Resume here runs");
            Check(!EditorApplication.isPaused, "The game runs again after Resume here");
            Check(recorder.History.NewestSequence == target, "The recording forgets the frames after the resumed one");
            Check(ball.transform.position == shownPosition, "The 3D body stays on the resumed frame");
            Check(Near(ball.linearVelocity, _expectedVelocity), $"Rigidbody velocity restored ({ball.linearVelocity} vs {_expectedVelocity})");
            Check(Near(disc.linearVelocity, _expectedVelocity2D), $"Rigidbody2D velocity restored ({disc.linearVelocity} vs {_expectedVelocity2D})");
            _afterResume = 0;
        }

        private static void FinishPlayMode()
        {
            var recorder = HindsightSession.Recorder;
            recorder.Commit();
            Check(recorder.History.FrameCount > ResumeFrame, "Recording carries on after Resume here");
            Check(HindsightSession.Save(RecordingPath) == null, "Saving the recording in Play Mode");
            Check(File.Exists(RecordingPath) && new FileInfo(RecordingPath).Length > 0, "The .hindsight file is written");
            FrameDriver.Tick = null;
            EditorApplication.ExitPlaymode();
        }

        private static void CheckOpenedRecording()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var ball = GameObject.Find("Ball 3D").transform;
            var editedPosition = ball.position;
            Check(!scene.isDirty, "The scene is clean before opening the recording");

            Check(HindsightSession.Open(RecordingPath) == null, "Opening the recording outside Play Mode");
            var recorder = HindsightSession.Recorder;
            Check(recorder != null && recorder.IsFromFile, "The window shows the saved recording");
            Check(recorder != null && recorder.MatchedObjects >= 3 && recorder.MissingObjects == 0,
                $"Objects found in the open scene ({recorder?.MatchedObjects} found, {recorder?.MissingObjects} missing)");

            Check(HindsightSession.ShowFrame(recorder.History.OldestSequence + 20), "Rewinding the saved recording");
            Check(ball.position != editedPosition, "The scene object moves to the recorded frame");
            Check(!scene.isDirty, "Rewinding outside Play Mode does not mark the scene as modified");
            HindsightSession.Close();
            Check(ball.position == editedPosition, "Closing puts the object back exactly");
            Check(!scene.isDirty, "The scene is still clean after closing");
            Finish();
        }

        private static void Finish()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            SessionState.SetBool(ActiveKey, false);
            AssetDatabase.DeleteAsset(ScenePath);
            var report = SessionState.GetString(ReportKey, string.Empty);
            File.WriteAllText(Path.Combine(Output, "report.txt"), report, Encoding.UTF8);
            EditorApplication.Exit(report.Contains("FAIL") ? 1 : 0);
        }

        #endregion


        #region Tools and Utilities

        private static Vector4 RecordedVelocity(Recorder recorder, Transform transform)
        {
            var streams = new List<int>();
            if (!recorder.TryGetSlot(transform, out var slot) || recorder.GetStreams(slot, streams) == 0)
                return Vector4.zero;
            NativeArray<Vector4> values = HindsightSession.Rewind.Values;
            return values[streams[0]];
        }

        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;

        private static void Check(bool passed, string what)
        {
            var line = $"{(passed ? "PASS" : "FAIL")}  {what}\n";
            SessionState.SetString(ReportKey, SessionState.GetString(ReportKey, string.Empty) + line);
            Debug.Log("[FeatureCheck] " + line.TrimEnd());
        }

        #endregion
    }
}
