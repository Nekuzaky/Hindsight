using Nekuzaky.Hindsight.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nekuzaky.Hindsight.Dev
{
    /// <summary>Writes Assets/Demo/Hindsight Demo.unity: a camera and the bootstrap that builds the rest at runtime.</summary>
    internal static class DemoSceneBuilder
    {
        public const string ScenePath = "Assets/Demo/Hindsight Demo.unity";

        [MenuItem("Tools/Hindsight/Development/Rebuild Demo Scene", priority = 200)]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera") { tag = "MainCamera" };
            var component = camera.AddComponent<Camera>();
            component.orthographic = true;
            component.orthographicSize = 5f;
            component.clearFlags = CameraClearFlags.SolidColor;
            component.backgroundColor = new Color(0.07f, 0.075f, 0.094f);
            camera.transform.position = new Vector3(0f, 0.4f, -10f);

            new GameObject("Hindsight Demo").AddComponent<DemoBootstrap>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Hindsight] Demo scene written to " + ScenePath);
        }
    }
}
