using System.Collections.Generic;
using UnityEngine;

namespace BigFish.NickG.Collisions
{
    // Resolve the pose proposed by the existing boat controller before its follow camera reads it.
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class BoatCollisionGuard : MonoBehaviour
    {
        const float Skin = 0.035f;
        const float Epsilon = 0.0001f;
        readonly RaycastHit[] casts = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[32];
        readonly HashSet<Collider> solids = new HashSet<Collider>();
        Bounds hull;
        Vector3 acceptedPosition;
        Quaternion acceptedRotation;
        bool configured;
        public Collider LastObstacle { get; private set; }

        public void Configure(Bounds localHull, IEnumerable<Collider> obstacles)
        {
            hull = localHull;
            solids.Clear();
            if (obstacles != null)
                foreach (Collider c in obstacles)
                    if (c != null && !c.transform.IsChildOf(transform)) solids.Add(c);
            configured = hull.size.x > 0 && hull.size.y > 0 && hull.size.z > 0;
            ResetSafePose();
        }

        // Call after an intentional teleport to a clear spawn location.
        public void ResetSafePose()
        {
            acceptedPosition = transform.position;
            acceptedRotation = transform.rotation;
            LastObstacle = null;
        }

        void LateUpdate() => ResolveCurrentPose();

        public bool ResolveCurrentPose()
        {
            if (!configured) return false;
            Vector3 proposed = transform.position;
            Quaternion proposedRotation = transform.rotation;
            LastObstacle = null;
            if (solids.Count == 0) { ResetSafePose(); return false; }
            if ((proposed - acceptedPosition).sqrMagnitude < Epsilon * Epsilon &&
                Quaternion.Angle(proposedRotation, acceptedRotation) < 0.01f) return false;

            Physics.SyncTransforms();
            Vector3 scale = transform.lossyScale;
            Vector3 center = Vector3.Scale(hull.center, scale);
            Vector3 half = Vector3.Scale(hull.extents,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            if (half.x <= Epsilon || half.y <= Epsilon || half.z <= Epsilon) return false;

            Vector3 position = acceptedPosition;
            Quaternion rotation = acceptedRotation;
            Vector3 delta = proposed - position;
            bool blocked = false;
            // Sweep the hull, then allow the unused horizontal motion to slide along one surface.
            for (int i = 0; i < 2 && delta.sqrMagnitude > Epsilon * Epsilon; i++)
            {
                float distance = delta.magnitude;
                Vector3 direction = delta / distance;
                if (!FirstHit(position + rotation * center, half, rotation, direction, distance, out RaycastHit hit))
                {
                    Vector3 candidate = position + delta;
                    Collider touching = Overlap(candidate + rotation * center, half, rotation);
                    if (touching == null) position = candidate;
                    else { blocked = true; LastObstacle = touching; }
                    break;
                }

                blocked = true;
                LastObstacle = hit.collider;
                float travel = Mathf.Clamp(hit.distance - Skin, 0f, distance);
                Vector3 reached = position + direction * travel;
                if (Overlap(reached + rotation * center, half, rotation) == null) position = reached;
                Vector3 leftover = delta - direction * travel;
                Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (normal.sqrMagnitude < Epsilon) break;
                leftover.y = 0f;
                delta = Vector3.ProjectOnPlane(leftover, normal.normalized);
            }

            float angle = Quaternion.Angle(rotation, proposedRotation);
            if (angle > 0.01f)
            {
                int steps = Mathf.CeilToInt(angle / 3f);
                Quaternion start = rotation;
                for (int step = 1; step <= steps; step++)
                {
                    Quaternion candidate = Quaternion.Slerp(start, proposedRotation, (float)step / steps);
                    Collider touching = Overlap(position + candidate * center, half, candidate);
                    if (touching != null) { blocked = true; LastObstacle = touching; break; }
                    rotation = candidate;
                }
            }

            acceptedPosition = position;
            acceptedRotation = rotation;
            if (blocked) transform.SetPositionAndRotation(position, rotation);
            else { acceptedPosition = proposed; acceptedRotation = proposedRotation; }
            return blocked;
        }

        bool IsObstacle(Collider c) => c != null && c.enabled && !c.isTrigger &&
            c.gameObject.activeInHierarchy && solids.Contains(c);

        bool FirstHit(Vector3 center, Vector3 half, Quaternion rotation, Vector3 direction,
            float distance, out RaycastHit nearest)
        {
            int count = Physics.BoxCastNonAlloc(center, half, direction, casts, rotation, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = casts;
            if (count == casts.Length)
            {
                hits = Physics.BoxCastAll(center, half, direction, rotation, distance,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            nearest = default;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsObstacle(hit.collider) || hit.distance >= best ||
                    Vector3.Dot(direction, hit.normal) >= -Epsilon) continue;
                nearest = hit;
                best = hit.distance;
            }
            return nearest.collider != null;
        }

        Collider Overlap(Vector3 center, Vector3 half, Quaternion rotation)
        {
            int count = Physics.OverlapBoxNonAlloc(center, half, overlaps, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            Collider[] items = overlaps;
            if (count == overlaps.Length)
            {
                items = Physics.OverlapBox(center, half, rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                count = items.Length;
            }
            for (int i = 0; i < count; i++) if (IsObstacle(items[i])) return items[i];
            return null;
        }
    }
}
