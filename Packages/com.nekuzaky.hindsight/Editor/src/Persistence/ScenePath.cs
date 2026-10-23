using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nekuzaky.Hindsight.Editor
{
    /// <summary>
    /// Where an object sits in its scene: the scene name, then the sibling index and name of each level from the
    /// root down. Used to find the same object again when a saved recording is opened.
    /// </summary>
    internal static class ScenePath
    {
        #region Public

        public const int MaxDepth = 256;

        /// <summary>Writes the path of <paramref name="transform"/>, or an empty path when it cannot be described.</summary>
        public static void Write(BinaryWriter writer, Transform transform, List<Transform> chain)
        {
            chain.Clear();
            for (var level = transform; level != null && chain.Count < MaxDepth; level = level.parent)
                chain.Add(level);

            var complete = transform != null && chain[chain.Count - 1].parent == null;
            writer.Write(complete ? transform.gameObject.scene.name ?? string.Empty : string.Empty);
            writer.Write(complete ? chain.Count : 0);
            if (!complete)
                return;
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                writer.Write(chain[i].GetSiblingIndex());
                writer.Write(chain[i].name);
            }
        }

        /// <summary>Reads a path and finds the object in the open scenes, or returns null.</summary>
        public static Transform Read(BinaryReader reader, List<GameObject> roots)
        {
            var sceneName = reader.ReadString();
            var depth = NativeIO.CheckCount(reader.ReadInt32(), MaxDepth, "hierarchy levels");
            Transform found = null;
            var lost = depth == 0;
            for (var level = 0; level < depth; level++)
            {
                var index = reader.ReadInt32();
                var name = reader.ReadString();
                if (lost)
                    continue;
                found = level == 0 ? FindRoot(sceneName, index, name, roots) : FindChild(found, index, name);
                lost = found == null;
            }
            return lost ? null : found;
        }

        #endregion


        #region Tools and Utilities

        private static Transform FindRoot(string sceneName, int index, string name, List<GameObject> roots)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.name != sceneName)
                    continue;
                scene.GetRootGameObjects(roots);
                if (index >= 0 && index < roots.Count && roots[index].name == name)
                    return roots[index].transform;
                foreach (var root in roots)
                {
                    if (root.name == name)
                        return root.transform;
                }
            }
            return null;
        }

        private static Transform FindChild(Transform parent, int index, string name)
        {
            if (index >= 0 && index < parent.childCount && parent.GetChild(index).name == name)
                return parent.GetChild(index);
            for (var i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name == name)
                    return parent.GetChild(i);
            }
            return null;
        }

        #endregion
    }
}
