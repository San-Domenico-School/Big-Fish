using System.Collections.Generic;
using BigFish.NickG.Collisions;
using UnityEngine;

namespace BigFish.NickG.HarborPlaytest
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-350)]
    public sealed class NickGHarborCollisions : MonoBehaviour
    {
        [Tooltip("The transform-driven player boat in this harbor scene.")]
        public Transform playerBoat;

        [Tooltip("The BigFishWorld model containing the coast, docks, and rocks.")]
        public Transform worldRoot;

        [Tooltip("The ocean surface, excluded from the solid obstacle list.")]
        public Transform waterSurface;

        [Tooltip("Optional solid scene models, such as the idle boat.")]
        public Transform[] extraObstacles;

        [Range(0.05f, 1f)]
        public float hullHeightFraction = 0.31f;

        public BoatCollisionGuard Guard { get; private set; }
        public int SolidCount { get; private set; }

        private Rigidbody boatBody;
        private bool madeKinematic;
        private CollisionDetectionMode originalDetectionMode;

        private void OnEnable()
        {
            if (playerBoat != null && worldRoot != null)
                Install();
        }

        /// <summary>Connects the selected live scene objects to the boat collision guard.</summary>
        public bool Install()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return false;

            if (Guard != null)
            {
                if (!Guard.enabled)
                {
                    UseTransformMotion();
                    Guard.ResetSafePose();
                    Guard.enabled = true;
                }

                return true;
            }

            if (!IsSceneObject(playerBoat) || !IsSolidRoot(worldRoot) ||
                !ColliderGeometry.TryGetBoatBounds(playerBoat, out Bounds hull, hullHeightFraction))
                return false;

            if (playerBoat.GetComponent<BoatCollisionGuard>() != null)
            {
                Debug.LogWarning("The harbor boat already has a collision guard.", this);
                return false;
            }

            var obstacles = new HashSet<Collider>();
            AddObstacles(worldRoot, obstacles);
            if (extraObstacles != null)
            {
                foreach (Transform root in extraObstacles)
                {
                    if (IsSolidRoot(root))
                        AddObstacles(root, obstacles);
                }
            }

            if (obstacles.Count == 0)
            {
                Debug.LogWarning("The harbor has no solid colliders to check against.", this);
                return false;
            }

            boatBody = playerBoat.GetComponent<Rigidbody>();
            UseTransformMotion();
            ColliderGeometry.EnsureBoatHull(playerBoat, hullHeightFraction);
            Physics.SyncTransforms();

            Guard = playerBoat.gameObject.AddComponent<BoatCollisionGuard>();
            Guard.Configure(hull, obstacles);
            SolidCount = obstacles.Count;
            return true;
        }

        private void OnDisable()
        {
            if (Guard != null)
                Guard.enabled = false;

            if (madeKinematic && boatBody != null)
            {
                boatBody.isKinematic = false;
                boatBody.collisionDetectionMode = originalDetectionMode;
            }

            madeKinematic = false;
        }

        private void UseTransformMotion()
        {
            if (boatBody == null || boatBody.isKinematic)
                return;

            // The harbor's BoatController applies its own height and transform each frame.
            // Keep the existing Rigidbody available for trigger events and stop gravity taking over.
            originalDetectionMode = boatBody.collisionDetectionMode;
            boatBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            boatBody.isKinematic = true;
            madeKinematic = true;
        }

        private void AddObstacles(Transform root, HashSet<Collider> obstacles)
        {
            ColliderGeometry.EnsureStaticObstacles(root);
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (ColliderGeometry.IsSolid(collider) && collider.attachedRigidbody == null &&
                    collider.attachedArticulationBody == null)
                    obstacles.Add(collider);
            }
        }

        private bool IsSolidRoot(Transform root)
        {
            return IsSceneObject(root) && !root.IsChildOf(playerBoat) && !playerBoat.IsChildOf(root) &&
                   (waterSurface == null || (!root.IsChildOf(waterSurface) && !waterSurface.IsChildOf(root)));
        }

        private bool IsSceneObject(Transform target)
        {
            return target != null && target.gameObject.scene == gameObject.scene && target.gameObject.activeInHierarchy;
        }
    }
}
