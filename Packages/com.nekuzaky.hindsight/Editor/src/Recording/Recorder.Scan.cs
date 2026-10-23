using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nekuzaky.Hindsight.Editor
{
    internal sealed partial class Recorder
    {
        #region Public

        public const int SweepPeriod = 30;

        public const int SweepBudget = 1024;

        public void MarkHierarchyDirty() => _scanEverything = true;

        public void SetPersistentSceneProbe(GameObject probe) => _probe = probe;

        #endregion


        #region Private and Protected

        private const int MaxScenes = 256;
        private const int MaxBranchDepth = 16;
        private const int MaxTailLength = 4096;

        private struct RootState
        {
            public int Count;
            public EntityId Signature;
        }

        private readonly List<GameObject> _roots = new(256);
        private readonly List<Transform> _hierarchy = new(1024);
        private readonly HashSet<EntityId> _ignored = new();
        private Dictionary<EntityId, RootState> _rootStates = new(256);
        private Dictionary<EntityId, RootState> _nextRootStates = new(256);
        private GameObject _probe;
        private bool _scanEverything = true;
        private bool _incomplete;
        private int _sweepCursor;
        private int _sweepBudget;
        private int _rootIndex;
        private int _capturedFrames;

        #endregion


        #region Tools and Utilities

        private void Scan()
        {
            var sequence = History.NewestSequence + 1;
            _sweepBudget = _capturedFrames++ % SweepPeriod == 0 ? SweepBudget : 0;
            _rootIndex = 0;
            if (_ignored.Count > 4 * Options.SlotLimit)
                _ignored.Clear();

            var sceneCount = Mathf.Min(SceneManager.sceneCount, MaxScenes);
            for (var i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    ScanScene(scene, sequence);
            }

            if (_probe != null && _probe.scene.IsValid())
                ScanScene(_probe.scene, sequence);

            if (_sweepBudget > 0)
                _sweepCursor = 0;
            _scanEverything = false;
            (_rootStates, _nextRootStates) = (_nextRootStates, _rootStates);
            _nextRootStates.Clear();
        }

        private void ScanScene(Scene scene, int sequence)
        {
            scene.GetRootGameObjects(_roots);
            foreach (var root in _roots)
            {
                if (root != null)
                    ScanRoot(root, _rootIndex++, sequence);
            }
            _roots.Clear();
        }

        private void ScanRoot(GameObject root, int index, int sequence)
        {
            var transform = root.transform;
            var state = new RootState { Count = transform.hierarchyCount, Signature = Signature(transform) };
            var id = root.GetEntityId();
            _incomplete = false;

            if (_scanEverything || !_rootStates.TryGetValue(id, out var known))
            {
                WalkSubtree(transform, sequence);
            }
            else if (known.Count != state.Count || known.Signature != state.Signature)
            {
                var found = WalkTail(transform, sequence);
                if (found == 0 && state.Count >= known.Count)
                    WalkSubtree(transform, sequence);
            }
            else if (_sweepBudget > 0 && index >= _sweepCursor && state.Count <= SweepBudget)
            {
                WalkSubtree(transform, sequence);
                _sweepBudget -= state.Count;
                _sweepCursor = index + 1;
            }

            if (!_incomplete)
                _nextRootStates[id] = state;
        }

        private int WalkTail(Transform root, int sequence)
        {
            var found = 0;
            var node = root;
            for (var depth = 0; depth < MaxBranchDepth && node != null; depth++)
            {
                var childCount = node.childCount;
                if (childCount == 0)
                    break;

                var stop = Mathf.Max(0, childCount - MaxTailLength);
                for (var i = childCount - 1; i >= stop; i--)
                {
                    var child = node.GetChild(i);
                    if (IsKnown(child))
                        break;
                    found += WalkSubtree(child, sequence);
                }
                node = node.GetChild(childCount - 1);
            }
            return found;
        }

        private int WalkSubtree(Transform top, int sequence)
        {
            top.GetComponentsInChildren(true, _hierarchy);
            var found = 0;
            foreach (var transform in _hierarchy)
            {
                if (IsKnown(transform))
                    continue;
                if (!ShouldRecord(transform))
                {
                    _ignored.Add(transform.GetEntityId());
                    found++;
                    continue;
                }
                if (Register(transform, sequence) < 0)
                {
                    _incomplete = true;
                    break;
                }
                found++;
            }
            _hierarchy.Clear();
            return found;
        }

        private bool IsKnown(Transform transform)
        {
            var id = transform.GetEntityId();
            return _slotById.ContainsKey(id) || _ignored.Contains(id);
        }

        private static EntityId Signature(Transform root)
        {
            var current = root;
            for (var depth = 0; depth < MaxBranchDepth; depth++)
            {
                var count = current.childCount;
                if (count == 0)
                    break;
                current = current.GetChild(count - 1);
            }
            return current.GetEntityId();
        }

        private bool ShouldRecord(Transform transform)
        {
            var gameObject = transform.gameObject;
            if ((gameObject.hideFlags & HideFlags.HideInHierarchy) != 0)
                return false;
            if (Options.IgnoreStatic && gameObject.isStatic)
                return false;
            if ((Options.LayerMask & (1 << gameObject.layer)) == 0)
                return false;
            return !Options.IgnoreCameras || !transform.TryGetComponent<Camera>(out _);
        }

        internal int Track(Transform transform)
        {
            if (TryGetSlot(transform, out var slot))
                return slot;
            return ShouldRecord(transform) ? Register(transform, History.NewestSequence + 1) : -1;
        }

        private void DisposeScanner()
        {
            _roots.Clear();
            _hierarchy.Clear();
            _ignored.Clear();
            _rootStates.Clear();
            _nextRootStates.Clear();
            _probe = null;
        }

        #endregion
    }
}
