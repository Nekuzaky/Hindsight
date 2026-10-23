using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Nekuzaky.Hindsight.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Nekuzaky.Hindsight.Dev
{
    /// <summary>
    /// Measures what recording costs in real Play Mode, with the real player loop. Each scenario runs twice, with
    /// recording off and on, at a fixed 60 frames per simulated second. Run it with Tools/Benchmark.ps1.
    /// </summary>
    [InitializeOnLoad]
    internal static class Benchmark
    {
        #region Private and Protected

        private const string ActiveKey = "Hindsight.Benchmark.Active";
        private const string OutputKey = "Hindsight.Benchmark.Output";
        private const int WarmupFrames = 120;
        private const int MeasuredFrames = 1800;
        private const int SeekSamples = 60;

        private sealed class Scenario
        {
            public string Name;
            public int Objects;
            public int Moving;
            public int Bodies;
            public int Churn;
        }

        private sealed class Result
        {
            public string Name;
            public double FrameOff;
            public double FrameOn;
            public double CostAverage;
            public double CostP95;
            public double CostMax;
            public long Allocated;
            public long Used;
            public long Reserved;
            public int Tracked;
            public double SeekMs;
            public double ExtractMs;
        }

        private static readonly Scenario[] Scenarios =
        {
            new() { Name = "10,000 objects, none moving", Objects = 10000, Moving = 0 },
            new() { Name = "1,000 objects, all moving", Objects = 1000, Moving = 1000 },
            new() { Name = "10,000 objects, 1,000 moving", Objects = 10000, Moving = 1000 },
            new() { Name = "10,000 objects, all moving", Objects = 10000, Moving = 10000 },
            new() { Name = "500 Rigidbody2D bodies", Bodies = 500 },
            new() { Name = "Churn: 20 objects created and 20 destroyed per frame", Objects = 1000, Churn = 20 }
        };

        private static readonly List<Result> Results = new();
        private static readonly double[] Costs = new double[MeasuredFrames];
        private static readonly List<Transform> Movers = new();
        private static readonly Queue<GameObject> Spawned = new();
        private const int ChurnLifetime = 30;

        private static int _scenario;
        private static bool _recording;
        private static int _frame;
        private static long _lastTimestamp;
        private static double _frameTotal;
        private static long _allocatedAtStart;
        private static GameObject _root;

        #endregion


        #region Main API

        static Benchmark()
        {
            if (!SessionState.GetBool(ActiveKey, false))
                return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (EditorApplication.isPlaying)
                Begin();
        }

        /// <summary>Entry point for -executeMethod.</summary>
        public static void Run()
        {
            var output = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "Output", "benchmark");
            Directory.CreateDirectory(output);
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(OutputKey, output);
            HindsightSettings.instance.RecordInPlayMode = true;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Frame Driver").AddComponent<FrameDriver>();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.EnterPlaymode();
        }

        #endregion


        #region Driving

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                Begin();
        }

        private static void Begin()
        {
            FrameDriver.Tick = Step;
            Time.captureFramerate = 60;
            _scenario = 0;
            _recording = false;
            Setup();
        }

        private static void Step()
        {
            var now = Stopwatch.GetTimestamp();
            var scenario = Scenarios[_scenario];
            Animate(scenario);

            if (_frame >= WarmupFrames)
            {
                var index = _frame - WarmupFrames;
                if (index > 0)
                    _frameTotal += RecorderStats.TicksToMilliseconds(now - _lastTimestamp);
                if (_recording && index > 0)
                    Costs[index - 1] = HindsightSession.Recorder.Stats.LastMilliseconds;
            }
            else if (_frame == WarmupFrames - 1 && _recording)
            {
                _allocatedAtStart = HindsightSession.Recorder.Stats.TotalAllocatedBytes;
            }

            _lastTimestamp = now;
            _frame++;
            if (_frame >= WarmupFrames + MeasuredFrames + 1)
                Finish();
        }

        private static void Setup()
        {
            var scenario = Scenarios[_scenario];
            if (_recording)
                HindsightSession.Start();
            else
                HindsightSession.Stop();

            _root = new GameObject("Benchmark Root");
            Movers.Clear();
            for (var i = 0; i < scenario.Objects; i++)
            {
                var child = new GameObject("Object").transform;
                child.SetParent(_root.transform, false);
                child.localPosition = new Vector3(i % 100, i / 100, 0f);
                if (i < scenario.Moving)
                    Movers.Add(child);
            }
            if (scenario.Bodies > 0)
                CreateBodies(scenario.Bodies);

            _frame = 0;
            _frameTotal = 0d;
            Array.Clear(Costs, 0, Costs.Length);
        }

        private static void CreateBodies(int count)
        {
            var floor = new GameObject("Floor");
            floor.transform.SetParent(_root.transform, false);
            floor.transform.position = new Vector3(0f, -20f, 0f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(400f, 1f);
            for (var i = 0; i < count; i++)
            {
                var body = new GameObject("Body");
                body.transform.SetParent(_root.transform, false);
                body.transform.position = new Vector3(i % 50 * 1.1f - 27f, i / 50 * 1.1f, 0f);
                body.AddComponent<CircleCollider2D>().radius = 0.45f;
                body.AddComponent<Rigidbody2D>();
            }
        }

        private static void Animate(Scenario scenario)
        {
            var t = _frame * (1f / 60f);
            // Bobbing and turning: three of the ten transform components change, as for a typical character.
            for (var i = 0; i < Movers.Count; i++)
            {
                Movers[i].SetLocalPositionAndRotation(new Vector3(i % 100, i / 100 + Mathf.Sin(t * 2f + i * 0.01f), 0f),
                    Quaternion.Euler(0f, t * 90f + i, 0f));
            }

            for (var i = 0; i < scenario.Churn; i++)
            {
                var bullet = new GameObject("Bullet");
                bullet.transform.SetParent(_root.transform, false);
                bullet.transform.localPosition = new Vector3(i, t, 0f);
                Spawned.Enqueue(bullet);
            }
            while (Spawned.Count > scenario.Churn * ChurnLifetime)
                Object.Destroy(Spawned.Dequeue());
            foreach (var bullet in Spawned)
                bullet.transform.localPosition += Vector3.up * 0.1f;
        }

        private static void Finish()
        {
            var scenario = Scenarios[_scenario];
            if (!_recording)
            {
                Results.Add(new Result { Name = scenario.Name, FrameOff = _frameTotal / MeasuredFrames });
            }
            else
            {
                Measure(Results[Results.Count - 1]);
            }

            Object.Destroy(_root);
            Spawned.Clear();
            if (_recording)
                _scenario++;
            _recording = !_recording;

            if (_scenario >= Scenarios.Length)
            {
                Write();
                SessionState.SetBool(ActiveKey, false);
                FrameDriver.Tick = null;
                EditorApplication.Exit(0);
                return;
            }
            Setup();
        }

        private static void Measure(Result result)
        {
            var recorder = HindsightSession.Recorder;
            result.FrameOn = _frameTotal / MeasuredFrames;
            var sorted = (double[])Costs.Clone();
            Array.Sort(sorted);
            var total = 0d;
            foreach (var cost in sorted)
                total += cost;
            result.CostAverage = total / sorted.Length;
            result.CostP95 = sorted[(int)(sorted.Length * 0.95)];
            result.CostMax = sorted[sorted.Length - 1];
            result.Allocated = recorder.Stats.TotalAllocatedBytes - _allocatedAtStart;
            result.Used = recorder.History.UsedBytes;
            result.Reserved = recorder.SizeInBytes;
            result.Tracked = recorder.AliveCount;
            MeasureSeeks(recorder, result);
        }

        private static void MeasureSeeks(Recorder recorder, Result result)
        {
            var history = recorder.History;
            var random = new System.Random(1);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < SeekSamples; i++)
                HindsightSession.Rewind.Show(history.OldestSequence + random.Next(history.FrameCount));
            watch.Stop();
            HindsightSession.Rewind.ReturnToLive();
            result.SeekMs = watch.Elapsed.TotalMilliseconds / SeekSamples;

            var samples = new TransformSample[history.FrameCount];
            watch.Restart();
            history.ExtractSlot(Math.Max(0, recorder.SlotCount - 1), samples);
            watch.Stop();
            result.ExtractMs = watch.Elapsed.TotalMilliseconds;
        }

        private static void Write()
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var text = new StringBuilder();
            text.AppendLine("| Scenario | Objects recorded | Hindsight per frame (avg / p95 / max) | Frame time off → on | Memory for 30 s | GC per frame | Seek | Graph data |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in Results)
            {
                text.AppendLine(string.Format(culture,
                    "| {0} | {1:N0} | {2:0.000} / {3:0.000} / {4:0.000} ms | {5:0.00} → {6:0.00} ms | {7:0.0} MB | {8} B | {9:0.00} ms | {10:0.00} ms |",
                    r.Name, r.Tracked, r.CostAverage, r.CostP95, r.CostMax, r.FrameOff, r.FrameOn,
                    r.Used / 1048576d, r.Allocated / MeasuredFrames, r.SeekMs, r.ExtractMs));
            }

            text.AppendLine();
            text.AppendLine(string.Format(culture, "Measured on {0}, {1} logical cores, Unity {2}, Editor Play Mode in batch mode, {3:N0} frames per scenario at 60 simulated fps after {4} warm-up frames.",
                SystemInfo.processorType.Trim(), SystemInfo.processorCount, Application.unityVersion, MeasuredFrames, WarmupFrames));

            var output = SessionState.GetString(OutputKey, Path.Combine("Tools", "Output", "benchmark"));
            File.WriteAllText(Path.Combine(output, "benchmark.md"), text.ToString());
            Debug.Log("[Hindsight] Benchmark written to " + output + "\n" + text);
        }

        #endregion
    }
}
