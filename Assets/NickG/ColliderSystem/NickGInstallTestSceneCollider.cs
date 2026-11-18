using System;
using System.Linq;
using BigFish.NickG.HarborPlaytest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigFish.NickG.Collisions
{
    internal static class NickGInstallTestSceneCollider
    {
        private const string ScenePath = "Assets/Scenes/Testing Scenes/NickG TestScene.unity";
        private const string MenuPath = "Tools/NickG/Add Collider to Test Scene";

        [MenuItem(MenuPath, true)]
        private static bool CanInstall() =>
            !EditorApplication.isPlayingOrWillChangePlaymode && SceneManager.GetActiveScene().path == ScenePath;

        [MenuItem(MenuPath)]
        private static void InstallFromMenu()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                Install(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Unity could not save NickG TestScene.");
            }
            catch (Exception error)
            {
                Debug.LogError("[NickG Collider] " + error.Message);
                EditorUtility.DisplayDialog("NickG Collider", error.Message, "OK");
            }
        }

        private static void Install(Scene scene)
        {
            if (scene.path != ScenePath) throw new InvalidOperationException("Open NickG TestScene first.");

            var roots = scene.GetRootGameObjects();
            var boat = FindRoot(roots, "FishingBoat").transform;
            var world = FindRoot(roots, "BigFishWorld").transform;
            var ocean = FindRoot(roots, "Ocean").transform;
            if (!ColliderGeometry.TryGetBoatBounds(boat, out _))
                throw new InvalidOperationException("Could not find the main FishingBoat hull mesh.");
            if (world.GetComponentInChildren<MeshFilter>() == null)
                throw new InvalidOperationException("Could not find the BigFishWorld harbor mesh.");

            var existing = roots.Select(go => go.GetComponent<NickGHarborCollisions>()).FirstOrDefault(c => c != null);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[NickG Collider] Collision setup is already installed in NickG TestScene.", existing);
                return;
            }

            var root = new GameObject("NickG Collider System");
            Undo.RegisterCreatedObjectUndo(root, "Add NickG Collider System");
            var setup = Undo.AddComponent<NickGHarborCollisions>(root);
            setup.playerBoat = boat;
            setup.worldRoot = world;
            setup.waterSurface = ocean;
            setup.extraObstacles = Array.Empty<Transform>();
            EditorUtility.SetDirty(setup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("[NickG Collider] Connected FishingBoat and BigFishWorld; ocean excluded.", root);
        }

        private static GameObject FindRoot(GameObject[] roots, string name)
        {
            var match = roots.FirstOrDefault(go => go.name == name);
            if (match == null) throw new InvalidOperationException("Missing scene object: " + name);
            return match;
        }
    }
}
