using UnityEngine;

namespace BigFish.NickG.Collisions
{
    /// <summary>Creates collision shapes for scene objects explicitly selected by the caller.</summary>
    public static class ColliderGeometry
    {
        private const float MinimumSize = 0.01f;

        /// <summary>Accepts enabled solid shapes in the scene, including authored boat colliders.</summary>
        public static bool IsSolid(Collider collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                return false;

            if (collider is MeshCollider mesh)
            {
                if (mesh.sharedMesh == null)
                    return false;

                Rigidbody body = mesh.attachedRigidbody;
                return mesh.convex || (mesh.attachedArticulationBody == null && (body == null || body.isKinematic));
            }

            return true;
        }

        /// <summary>Adds a root-local box if the active boat has no solid shape.</summary>
        public static int EnsureBoatHull(Transform boatRoot, float hullHeightFraction = 1f)
        {
            if (!IsActiveSceneObject(boatRoot))
                return 0;

            foreach (Collider collider in boatRoot.GetComponentsInChildren<Collider>())
            {
                if (IsSolid(collider))
                    return 0;
            }

            if (!TryGetBoatBounds(boatRoot, out Bounds bounds, hullHeightFraction))
                return 0;

            BoxCollider hull = boatRoot.gameObject.AddComponent<BoxCollider>();
            hull.center = bounds.center;
            hull.size = bounds.size;
            return 1;
        }

        /// <summary>
        /// Fits active boat meshes in root-local coordinates. The height fraction keeps the lower
        /// part of the bounds when above-water details should be excluded from a sweep.
        /// </summary>
        public static bool TryGetBoatBounds(Transform boatRoot, out Bounds bounds, float hullHeightFraction = 1f)
        {
            bounds = default;
            if (boatRoot == null || !HasOrthogonalAxes(boatRoot) || !IsFinite(hullHeightFraction) || hullHeightFraction <= 0f)
                return false;

            Matrix4x4 worldToBoat = boatRoot.worldToLocalMatrix;
            bool found = false;
            foreach (MeshFilter filter in boatRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh != null && mesh.vertexCount > 0)
                    IncludeBounds(mesh.bounds, worldToBoat * filter.transform.localToWorldMatrix, ref bounds, ref found);
            }

            foreach (SkinnedMeshRenderer renderer in boatRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.sharedMesh != null)
                    IncludeBounds(renderer.localBounds, worldToBoat * renderer.transform.localToWorldMatrix, ref bounds, ref found);
            }

            if (!found)
            {
                foreach (Collider collider in boatRoot.GetComponentsInChildren<Collider>())
                {
                    if (IsSolid(collider))
                        IncludeBounds(collider.bounds, worldToBoat, ref bounds, ref found);
                }
            }

            if (!found || bounds.size.x < MinimumSize || bounds.size.z < MinimumSize)
                return false;

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            max.y = min.y + Mathf.Max(MinimumSize, bounds.size.y * Mathf.Min(1f, hullHeightFraction));
            bounds.SetMinMax(min, max);
            return true;
        }

        /// <summary>
        /// Adds triangle mesh colliders to active MeshFilters in a selected obstacle subtree.
        /// Select only physical scenery. Existing colliders and triggers keep their settings.
        /// Skinned, moving, and sheared obstacle models require an authored collision proxy.
        /// </summary>
        public static int EnsureStaticObstacles(Transform modelRoot)
        {
            if (!IsActiveSceneObject(modelRoot))
                return 0;

            int added = 0;
            foreach (MeshFilter filter in modelRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (!HasTriangleGeometry(mesh) || !HasOrthogonalAxes(filter.transform))
                    continue;

                if (filter.GetComponentInParent<Rigidbody>() != null || filter.GetComponentInParent<ArticulationBody>() != null)
                    continue;

                if (HasSolidCollider(filter))
                    continue;

                // Use default cooking and the imported mesh. Vertex arrays and importer changes
                // are unnecessary for ordinary static concave meshes, including nonreadable assets.
                MeshCollider obstacle = filter.gameObject.AddComponent<MeshCollider>();
                if (obstacle.sharedMesh != mesh)
                    obstacle.sharedMesh = mesh;
                added++;
            }

            return added;
        }

        private static bool IsActiveSceneObject(Transform root)
        {
            return root != null && root.gameObject.scene.IsValid() && root.gameObject.scene.isLoaded &&
                   root.gameObject.activeInHierarchy;
        }

        private static bool HasSolidCollider(Component component)
        {
            foreach (Collider collider in component.GetComponents<Collider>())
            {
                if (IsSolid(collider))
                    return true;
            }

            return false;
        }

        private static bool HasTriangleGeometry(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 3)
                return false;

            bool hasTriangles = false;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                uint indices = mesh.GetIndexCount(submesh);
                if (indices == 0)
                    continue;

                if (mesh.GetTopology(submesh) != MeshTopology.Triangles || indices < 3)
                    return false;

                hasTriangles = true;
            }

            return hasTriangles;
        }

        private static void IncludeBounds(Bounds source, Matrix4x4 localToBoat, ref Bounds combined, ref bool found)
        {
            Vector3 min = source.min;
            Vector3 max = source.max;
            Bounds transformed = default;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = localToBoat.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));

                if (!IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z))
                    return;

                if (corner == 0)
                    transformed = new Bounds(point, Vector3.zero);
                else
                    transformed.Encapsulate(point);
            }

            if (found)
                combined.Encapsulate(transformed);
            else
                combined = transformed;

            found = true;
        }

        private static bool HasOrthogonalAxes(Transform transform)
        {
            Matrix4x4 matrix = transform.localToWorldMatrix;
            Vector3 x = matrix.GetColumn(0);
            Vector3 y = matrix.GetColumn(1);
            Vector3 z = matrix.GetColumn(2);
            float sx = x.magnitude;
            float sy = y.magnitude;
            float sz = z.magnitude;
            if (!IsFinite(sx) || !IsFinite(sy) || !IsFinite(sz) || sx <= 0.00001f || sy <= 0.00001f || sz <= 0.00001f)
                return false;

            const float tolerance = 0.001f;
            return Mathf.Abs(Vector3.Dot(x / sx, y / sy)) < tolerance &&
                   Mathf.Abs(Vector3.Dot(x / sx, z / sz)) < tolerance &&
                   Mathf.Abs(Vector3.Dot(y / sy, z / sz)) < tolerance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
