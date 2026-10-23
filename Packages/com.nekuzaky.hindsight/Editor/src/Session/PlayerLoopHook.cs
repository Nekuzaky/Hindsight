using System;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Adds Hindsight's callbacks to Unity's player loop: one at the start of a frame, which collects the previous
    /// capture, one right after LateUpdate, which schedules the capture so it runs while the frame renders, and one
    /// at the very end of the frame. No GameObject is added to your scene.
    /// </summary>
    internal static class PlayerLoopHook
    {
        #region Private and Protected

        private struct HindsightCommit { }

        private struct HindsightCapture { }

        private struct HindsightFrameEnd { }


        #endregion


        #region Main API

        public static bool IsInstalled { get; private set; }

        public static void Install(PlayerLoopSystem.UpdateFunction frameStart, PlayerLoopSystem.UpdateFunction afterLateUpdate,
            PlayerLoopSystem.UpdateFunction frameEnd)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);

            var inserted = Insert(ref loop, typeof(Initialization), System(typeof(HindsightCommit), frameStart), true)
                && Insert(ref loop, typeof(PostLateUpdate), System(typeof(HindsightCapture), afterLateUpdate), true)
                && Insert(ref loop, typeof(PostLateUpdate), System(typeof(HindsightFrameEnd), frameEnd), false);
            if (!inserted)
                throw new InvalidOperationException("Hindsight could not find the Initialization and PostLateUpdate phases of the player loop.");

            PlayerLoop.SetPlayerLoop(loop);
            IsInstalled = true;
        }

        public static void Uninstall()
        {
            if (!IsInstalled)
                return;

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
            IsInstalled = false;
        }

        #endregion


        #region Tools and Utilities

        private static PlayerLoopSystem System(Type type, PlayerLoopSystem.UpdateFunction update)
        {
            return new PlayerLoopSystem { type = type, updateDelegate = update };
        }

        private static bool Insert(ref PlayerLoopSystem loop, Type phase, PlayerLoopSystem system, bool atStart)
        {
            var phases = loop.subSystemList;
            if (phases == null)
                return false;

            for (var i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != phase)
                    continue;

                var current = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var updated = new PlayerLoopSystem[current.Length + 1];
                Array.Copy(current, 0, updated, atStart ? 1 : 0, current.Length);
                updated[atStart ? 0 : current.Length] = system;
                phases[i].subSystemList = updated;
                return true;
            }
            return false;
        }

        /// <summary>Removes Hindsight's systems from every phase. The loop is two levels deep, so no recursion.</summary>
        private static void Remove(ref PlayerLoopSystem loop)
        {
            var phases = loop.subSystemList;
            if (phases == null)
                return;

            for (var i = 0; i < phases.Length; i++)
            {
                var systems = phases[i].subSystemList;
                if (systems == null)
                    continue;

                var kept = 0;
                foreach (var system in systems)
                {
                    if (!IsOurs(system))
                        kept++;
                }
                if (kept == systems.Length)
                    continue;

                var filtered = new PlayerLoopSystem[kept];
                var index = 0;
                foreach (var system in systems)
                {
                    if (!IsOurs(system))
                        filtered[index++] = system;
                }
                phases[i].subSystemList = filtered;
            }
        }

        private static bool IsOurs(in PlayerLoopSystem system)
        {
            return system.type == typeof(HindsightCommit) || system.type == typeof(HindsightCapture)
                || system.type == typeof(HindsightFrameEnd);
        }

        #endregion
    }
}
