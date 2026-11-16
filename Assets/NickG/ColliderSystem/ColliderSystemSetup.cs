using System.Collections.Generic;
using UnityEngine;

namespace BigFish.NickG.Collisions
{
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class ColliderSystemSetup : MonoBehaviour
    {
        [Tooltip("Assign the existing boat, or leave empty when a spawner creates it.")]
        public Transform boat;
        public string spawnedBoatName = "PlayerBoat";
        [Tooltip("Select only solid models, such as shoreline, dock, rocks, and buildings. Leave water and gameplay triggers out.")]
        public Transform[] solidModels;
        [Range(0.05f, 1f)] public float hullHeightFraction = 0.31f;

        public BoatCollisionGuard Guard { get; private set; }
        public int SolidCount => solids.Count;
        readonly HashSet<Collider> solids = new HashSet<Collider>();
        float nextSearch;

        void Awake() => PrepareObstacles();

        void Update()
        {
            if (Guard != null || Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 0.25f;
            Transform target = boat;
            if (target == null && !string.IsNullOrWhiteSpace(spawnedBoatName))
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                    if (root.activeInHierarchy && root.name == spawnedBoatName) { target = root.transform; break; }
            if (target != null) InstallBoat(target);
        }

        public void PrepareObstacles()
        {
            solids.Clear();
            if (solidModels != null)
                foreach (Transform root in solidModels)
                {
                    if (root == null || root == boat) continue;
                    ColliderGeometry.EnsureStaticObstacles(root);
                    foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                        if (ColliderGeometry.IsSolid(collider) && collider.attachedRigidbody == null)
                            solids.Add(collider);
                }
            Physics.SyncTransforms();
        }

        public bool InstallBoat(Transform target)
        {
            if (target == null || !ColliderGeometry.TryGetBoatBounds(target, out Bounds bounds, hullHeightFraction))
                return false;
            ColliderGeometry.EnsureBoatHull(target, hullHeightFraction);
            Guard = target.GetComponent<BoatCollisionGuard>();
            if (Guard == null) Guard = target.gameObject.AddComponent<BoatCollisionGuard>();
            Guard.Configure(bounds, solids);
            return true;
        }
    }
}
