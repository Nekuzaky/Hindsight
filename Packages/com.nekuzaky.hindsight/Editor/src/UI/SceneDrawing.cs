using System;
using UnityEditor;
using UnityEngine;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Scene view drawing: the motion trail of the selected objects over the recording, with the displayed frame
    /// and the error markers on it, and ghosts where destroyed objects were at the displayed frame.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneDrawing
    {
        #region Private and Protected

        private const int MaxTrails = 8;
        private const int MaxTrailPoints = 2048;
        private const int ChunkPoints = 256;
        private const int MaxGhosts = 256;
        private const double LiveRefreshInterval = 0.25d;

        private struct Trail
        {
            public int Slot;
            public int Count;
            public Vector3[] Points;
            public int[] Sequences;
        }

        private static readonly Trail[] Trails = new Trail[MaxTrails];
        private static readonly Vector3[] Chunk = new Vector3[ChunkPoints];
        private static TransformSample[] _samples = Array.Empty<TransformSample>();
        private static int _trailCount;
        private static int _builtOldest = -1;
        private static int _builtNewest = -1;
        private static int _builtSelection;
        private static double _lastBuild;

        #endregion


        #region Main API

        static SceneDrawing()
        {
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged += () => _builtSelection = -1;
        }

        #endregion


        #region Drawing

        private static void OnSceneGui(SceneView view)
        {
            var recorder = HindsightSession.Recorder;
            if (recorder == null || recorder.History.IsEmpty || Event.current.type != EventType.Repaint)
                return;

            var settings = HindsightSettings.instance;
            if (settings.ShowMotionTrail)
            {
                RebuildTrailsIfNeeded(recorder);
                for (var i = 0; i < _trailCount; i++)
                    DrawTrail(recorder, Trails[i]);
            }

            if (settings.ShowDestroyedGhosts && Navigation.IsRewinding)
                DrawGhosts(recorder);
        }

        private static void DrawTrail(Recorder recorder, in Trail trail)
        {
            if (trail.Count < 2)
                return;

            // The path already travelled at the displayed frame is solid, the rest of the recording is faint.
            var current = NearestPoint(trail, Navigation.Displayed);
            var past = Palette.Accent;
            var future = Palette.Accent;
            future.a = 0.3f;
            DrawRange(trail, 0, current, past, 3.5f);
            DrawRange(trail, Mathf.Max(0, current), trail.Count - 1, future, 2f);

            DrawTrailMarkers(recorder, trail);
            DrawCurrent(trail);
        }

        private static void DrawRange(in Trail trail, int from, int to, Color color, float width)
        {
            if (to - from < 1)
                return;

            Handles.color = color;
            for (var start = from; start < to; start += ChunkPoints - 1)
            {
                var count = Mathf.Min(ChunkPoints, to - start + 1);
                Array.Copy(trail.Points, start, Chunk, 0, count);
                Handles.DrawAAPolyLine(width, count, Chunk);
            }
        }

        private static void DrawTrailMarkers(Recorder recorder, in Trail trail)
        {
            var markers = recorder.Markers;
            for (var i = 0; i < markers.Count; i++)
            {
                ref readonly var marker = ref markers[i];
                if (!marker.IsError && marker.Kind != MarkerKind.Bookmark)
                    continue;
                var index = NearestPoint(trail, marker.Sequence);
                if (index < 0)
                    continue;
                var point = trail.Points[index];
                Handles.color = Palette.Of(marker.Kind);
                Handles.DrawSolidDisc(point, Camera.current.transform.forward, HandleUtility.GetHandleSize(point) * 0.06f);
            }
        }

        private static void DrawCurrent(in Trail trail)
        {
            var index = NearestPoint(trail, Navigation.Displayed);
            if (index < 0)
                return;
            var point = trail.Points[index];
            var size = HandleUtility.GetHandleSize(point);
            Handles.color = Navigation.IsRewinding ? Palette.Accent : Palette.Live;
            Handles.DrawSolidDisc(point, Camera.current.transform.forward, size * 0.07f);
            Handles.color = Color.white;
            Handles.DrawWireDisc(point, Camera.current.transform.forward, size * 0.1f, 2f);
        }

        private static void DrawGhosts(Recorder recorder)
        {
            var sequence = Navigation.Displayed;
            var states = HindsightSession.Rewind.Transforms;
            var drawn = 0;
            Handles.color = new Color(1f, 1f, 1f, 0.55f);
            for (var slot = 0; slot < recorder.SlotCount && drawn < MaxGhosts; slot++)
            {
                if (recorder.IsAliveSlot(slot) || !recorder.WasRoot(slot) || !recorder.ExistedAt(slot, sequence))
                    continue;
                var state = states[slot];
                if (!state.IsSet)
                    continue;

                var size = HandleUtility.GetHandleSize(state.Position) * 0.12f;
                Handles.DrawDottedLine(state.Position + Vector3.left * size, state.Position + Vector3.right * size, 2f);
                Handles.DrawDottedLine(state.Position + Vector3.down * size, state.Position + Vector3.up * size, 2f);
                Handles.DrawWireDisc(state.Position, Camera.current.transform.forward, size, 1.5f);
                drawn++;
            }
        }

        #endregion


        #region Tools and Utilities

        private static void RebuildTrailsIfNeeded(Recorder recorder)
        {
            var history = recorder.History;
            var active = Selection.activeTransform;
            var selection = (active != null ? active.GetInstanceID() : 0) ^ Selection.transforms.Length;
            var stale = history.OldestSequence != _builtOldest || history.NewestSequence != _builtNewest || selection != _builtSelection;
            var now = EditorApplication.timeSinceStartup;
            if (!stale || (!EditorApplication.isPaused && selection == _builtSelection && now - _lastBuild < LiveRefreshInterval))
                return;

            _lastBuild = now;
            _builtOldest = history.OldestSequence;
            _builtNewest = history.NewestSequence;
            _builtSelection = selection;
            _trailCount = 0;

            if (_samples.Length < history.FrameCount)
                _samples = new TransformSample[Mathf.NextPowerOfTwo(history.FrameCount)];

            foreach (var transform in Selection.transforms)
            {
                if (_trailCount >= MaxTrails)
                    break;
                if (transform != null && recorder.TryGetSlot(transform, out var slot))
                    BuildTrail(recorder, transform, slot, ref Trails[_trailCount++]);
            }
        }

        private static void BuildTrail(Recorder recorder, Transform transform, int slot, ref Trail trail)
        {
            var history = recorder.History;
            var frames = history.ExtractSlot(slot, _samples);
            var step = Mathf.Max(1, Mathf.CeilToInt(frames / (float)MaxTrailPoints));
            trail.Points ??= new Vector3[MaxTrailPoints + 1];
            trail.Sequences ??= new int[MaxTrailPoints + 1];
            trail.Slot = slot;
            trail.Count = 0;

            var parent = transform.parent;
            for (var i = 0; i < frames && trail.Count < MaxTrailPoints; i += step)
            {
                var sample = _samples[i];
                if (!sample.IsSet)
                    continue;
                var point = parent != null ? parent.TransformPoint(sample.Position) : sample.Position;
                if (trail.Count > 0 && trail.Points[trail.Count - 1] == point)
                    continue;
                trail.Points[trail.Count] = point;
                trail.Sequences[trail.Count] = history.OldestSequence + i;
                trail.Count++;
            }
        }

        private static int NearestPoint(in Trail trail, int sequence)
        {
            if (trail.Count == 0 || sequence < trail.Sequences[0])
                return -1;

            int low = 0, high = trail.Count - 1;
            while (low < high)
            {
                var middle = (low + high + 1) >> 1;
                if (trail.Sequences[middle] <= sequence)
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }

        #endregion
    }
}
