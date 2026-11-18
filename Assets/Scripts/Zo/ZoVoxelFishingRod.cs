using UnityEngine;

namespace BigFish
{
    /// <summary>
    /// Builds a blocky, voxel-style fishing rod out of cubes at runtime.
    /// Put this on the rod pivot under the camera.
    ///
    /// Hierarchy it builds:
    ///   (this)                 - pivot
    ///     Swing                - rotated by SetSwing() for cast / reel animations
    ///       Voxels             - aligned so the rod points along Rod Direction
    ///         cubes..., ReelHandle, Tip
    /// </summary>
    public class ZoVoxelFishingRod : MonoBehaviour
    {
        [Tooltip("Rod direction in camera space (x = right, y = up, z = forward).")]
        [SerializeField] Vector3 rodDirection = new Vector3(-0.25f, 0.8f, 1f);

        [Tooltip("Size of one voxel. The whole rod is about 30 voxels long.")]
        [SerializeField] float voxel = 0.045f;

        [Min(2)] [SerializeField] int handleVoxels = 8;
        [Min(4)] [SerializeField] int poleVoxels = 22;

        [Header("Colors")]
        [SerializeField] Color gripColor = new Color(0.22f, 0.13f, 0.07f);
        [SerializeField] Color gripStripeColor = new Color(0.12f, 0.07f, 0.04f);
        [SerializeField] Color poleColor = new Color(0.58f, 0.38f, 0.2f);
        [SerializeField] Color poleTipColor = new Color(0.8f, 0.62f, 0.38f);
        [SerializeField] Color metalColor = new Color(0.62f, 0.64f, 0.7f);
        [SerializeField] Color knobColor = new Color(0.85f, 0.2f, 0.15f);

        public Transform Tip { get; private set; }

        Transform _swing;
        Transform _reelHandle;

        void Awake()
        {
            Build();
        }

        public void Build()
        {
            if (_swing != null)
                return;

            _swing = new GameObject("Swing").transform;
            _swing.SetParent(transform, false);

            Transform voxels = new GameObject("Voxels").transform;
            voxels.SetParent(_swing, false);
            voxels.localRotation = Quaternion.FromToRotation(Vector3.up, rodDirection.normalized);

            float v = voxel;

            Material grip = ZoVoxels.Mat(gripColor);
            Material stripe = ZoVoxels.Mat(gripStripeColor);
            Material pole = ZoVoxels.Mat(poleColor);
            Material tip = ZoVoxels.Mat(poleTipColor);
            Material metal = ZoVoxels.Mat(metalColor);
            Material knob = ZoVoxels.Mat(knobColor);

            // Handle: chunky 2x2 voxels with stripes, rod's local +Y runs up the rod
            for (int i = 0; i < handleVoxels; i++)
            {
                ZoVoxels.Cube(voxels, new Vector3(0f, (i + 0.5f) * v, 0f),
                    new Vector3(2f * v, v, 2f * v), i % 3 == 1 ? stripe : grip);
            }

            // Butt cap
            ZoVoxels.Cube(voxels, new Vector3(0f, -0.5f * v, 0f), Vector3.one * 2.4f * v, stripe);

            // Reel: hangs off the +Z side (that ends up underneath the rod)
            float reelY = handleVoxels * 0.6f * v;
            ZoVoxels.Cube(voxels, new Vector3(0f, reelY, 1.5f * v), new Vector3(v, v, v), metal);           // foot
            ZoVoxels.Cube(voxels, new Vector3(0f, reelY, 2.75f * v), new Vector3(2.5f * v, 2.5f * v, 1.5f * v), metal); // spool

            _reelHandle = new GameObject("ReelHandle").transform;
            _reelHandle.SetParent(voxels, false);
            _reelHandle.localPosition = new Vector3(1.75f * v, reelY, 2.75f * v);
            ZoVoxels.Cube(_reelHandle, new Vector3(0f, 0.75f * v, 0f), new Vector3(0.5f * v, 1.5f * v, 0.5f * v), metal);
            ZoVoxels.Cube(_reelHandle, new Vector3(0.5f * v, 1.5f * v, 0f), new Vector3(v, 0.8f * v, 0.8f * v), knob);

            // Pole: single voxels, a little thicker at the base
            float poleStart = handleVoxels * v;
            for (int i = 0; i < poleVoxels; i++)
            {
                float t = i / (float)(poleVoxels - 1);
                float thickness = t < 0.35f ? 1.3f * v : v;
                ZoVoxels.Cube(voxels, new Vector3(0f, poleStart + (i + 0.5f) * v, 0f),
                    new Vector3(thickness, v, thickness), t > 0.8f ? tip : pole);

                // Line guides every few voxels
                if (i > 0 && i % 6 == 0)
                    ZoVoxels.Cube(voxels, new Vector3(0f, poleStart + (i + 0.5f) * v, 1f * v),
                        Vector3.one * 0.6f * v, metal);
            }

            Tip = new GameObject("Tip").transform;
            Tip.SetParent(voxels, false);
            Tip.localPosition = new Vector3(0f, poleStart + poleVoxels * v, 0f);
        }

        /// <summary>Pitch: negative swings the tip up/back toward you, positive whips it forward.</summary>
        public void SetSwing(float pitchDegrees, float rollDegrees = 0f)
        {
            if (_swing != null)
                _swing.localRotation = Quaternion.Euler(pitchDegrees, 0f, rollDegrees);
        }

        public void SpinReel(float degrees)
        {
            if (_reelHandle != null)
                _reelHandle.Rotate(Vector3.right, degrees, Space.Self);
        }

        public void SetVisible(bool visible)
        {
            if (_swing != null)
                _swing.gameObject.SetActive(visible);
        }
    }

    /// <summary>Tiny helpers for building cube-based props.</summary>
    public static class ZoVoxels
    {
        public static Transform Cube(Transform parent, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        public static Material Mat(Color c)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var m = new Material(shader);
            m.color = c;
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", 0.1f);
            return m;
        }
    }
}
