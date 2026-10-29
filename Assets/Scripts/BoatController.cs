using UnityEngine;
using UnityEngine.InputSystem;

namespace BigFish
{
    /// <summary>
    /// WASD boat handling, wake, and a simple swell the hull rides on.
    /// Uses Unity's NEW Input System.
    /// </summary>
    public class BoatController : MonoBehaviour
    {
        [Header("Handling")]
        public float TurnRate = 62f;
        public float Acceleration = 9f;
        public float MaxSpeed = 12f;
        public float Drag = 1.6f;

        [Header("Water")]
        public float WaterLevel = 0f;
        public float WaveHeight = 0.25f;
        public float WaveSpeed = 1.2f;

        [Header("Effects")]
        public bool ShowWake = true;

        // Model is roughly this long (used to place wake / spray)
        public float BoatLength = 4f;

        public float Speed { get; private set; }

        public float Speed01 =>
            Mathf.Clamp01(
                Mathf.Abs(Speed) /
                Mathf.Max(1f, MaxSpeed)
            );

        // Locked while a fish is on the line
        public bool Anchored;

        private ParticleSystem _wake;
        private ParticleSystem _spray;

        private float _bank;

        // ==============================
        // INITIALIZATION
        // ==============================

        void Start()
        {
            if (ShowWake)
                BuildWake();
        }

        // ==============================
        // WATER PARTICLE EFFECTS
        // ==============================

        void BuildWake()
        {
            // Wake behind the boat
            var wakeGo = new GameObject("Wake");

            wakeGo.transform.SetParent(transform, false);

            wakeGo.transform.localPosition =
                new Vector3(
                    0f,
                    0.1f,
                    -BoatLength * 0.5f - 0.4f
                );

            wakeGo.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);

            _wake = wakeGo.AddComponent<ParticleSystem>();

            var main = _wake.main;

            main.startLifetime = 2.6f;
            main.startSpeed = 1.2f;
            main.startSize = 3.1f;
            main.startColor =
                new Color(1f, 1f, 1f, 0.65f);

            main.simulationSpace =
                ParticleSystemSimulationSpace.World;

            main.maxParticles = 400;

            var emission = _wake.emission;
            emission.rateOverTime = 0f;

            var shape = _wake.shape;

            shape.shapeType =
                ParticleSystemShapeType.Cone;

            shape.angle = 24f;
            shape.radius = 1.4f;

            var col = _wake.colorOverLifetime;
            col.enabled = true;

            var grad = new Gradient();

            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );

            col.color = grad;

            var sol = _wake.sizeOverLifetime;
            sol.enabled = true;

            sol.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.Linear(
                    0f, 0.4f,
                    1f, 1.6f
                )
            );

            var wr =
                _wake.GetComponent<ParticleSystemRenderer>();

            wr.renderMode =
                ParticleSystemRenderMode.Billboard;

            wr.material = MakeParticleMaterial(
                new Color(0.94f, 0.99f, 1f, 1f)
            );

            wr.alignment =
                ParticleSystemRenderSpace.World;

            // Bow spray at front of boat
            var sprayGo = new GameObject("BowSpray");

            sprayGo.transform.SetParent(transform, false);

            sprayGo.transform.localPosition =
                new Vector3(
                    0f,
                    0.2f,
                    BoatLength * 0.5f - 0.3f
                );

            _spray =
                sprayGo.AddComponent<ParticleSystem>();

            var m2 = _spray.main;

            m2.startLifetime = 0.8f;
            m2.startSpeed = 4f;
            m2.startSize = 0.5f;
            m2.gravityModifier = 1.4f;

            m2.startColor =
                new Color(1f, 1f, 1f, 0.8f);

            m2.simulationSpace =
                ParticleSystemSimulationSpace.World;

            var e2 = _spray.emission;
            e2.rateOverTime = 0f;

            var s2 = _spray.shape;

            s2.shapeType =
                ParticleSystemShapeType.Cone;

            s2.angle = 30f;
            s2.radius = 0.8f;

            _spray
                .GetComponent<ParticleSystemRenderer>()
                .material =
                    MakeParticleMaterial(Color.white);
        }

        public static Material MakeParticleMaterial(Color c)
        {
            // URP particle shader, with a built-in fallback
            Shader shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");

            var mat = new Material(shader);
            mat.color = c;

            return mat;
        }

        // ==============================
        // NEW INPUT SYSTEM
        // ==============================

        private void ReadBoatInput(
            out float throttle,
            out float steer
        )
        {
            throttle = 0f;
            steer = 0f;

            // Get currently connected keyboard
            Keyboard keyboard = Keyboard.current;

            // No keyboard connected
            if (keyboard == null)
                return;

            // Forward: W or Up Arrow
            if (
                keyboard.wKey.isPressed ||
                keyboard.upArrowKey.isPressed
            )
            {
                throttle += 1f;
            }

            // Reverse: S or Down Arrow
            if (
                keyboard.sKey.isPressed ||
                keyboard.downArrowKey.isPressed
            )
            {
                throttle -= 0.6f;
            }

            // Steer left: A or Left Arrow
            if (
                keyboard.aKey.isPressed ||
                keyboard.leftArrowKey.isPressed
            )
            {
                steer -= 1f;
            }

            // Steer right: D or Right Arrow
            if (
                keyboard.dKey.isPressed ||
                keyboard.rightArrowKey.isPressed
            )
            {
                steer += 1f;
            }
        }

        // ==============================
        // OCEAN SWELL
        // ==============================

        float WaveHeightAt(float x, float z, float t)
        {
            return WaterLevel +
                   WaveHeight * (
                       Mathf.Sin(x * 0.15f + t * WaveSpeed) +
                       Mathf.Sin(z * 0.11f + t * WaveSpeed * 0.8f)
                   ) * 0.5f;
        }

        Vector3 WaveNormalAt(float x, float z, float t)
        {
            const float e = 0.5f;

            float hL = WaveHeightAt(x - e, z, t);
            float hR = WaveHeightAt(x + e, z, t);
            float hD = WaveHeightAt(x, z - e, t);
            float hU = WaveHeightAt(x, z + e, t);

            return new Vector3(
                hL - hR,
                2f * e,
                hD - hU
            ).normalized;
        }

        // ==============================
        // MAIN UPDATE LOOP
        // ==============================

        void Update()
        {
            float dt = Time.deltaTime;

            // ==============================
            // PLAYER INPUT
            // ==============================

            float throttle = 0f;
            float steer = 0f;

            if (!Anchored)
            {
                // Read keyboard using NEW Input System
                ReadBoatInput(
                    out throttle,
                    out steer
                );
            }

            // ==============================
            // BOAT ACCELERATION
            // ==============================

            Speed +=
                throttle * Acceleration * dt;

            // Water resistance
            Speed -= Speed * Drag * dt;

            // Maximum forward/reverse speed
            Speed = Mathf.Clamp(
                Speed,
                -MaxSpeed * 0.45f,
                MaxSpeed
            );

            // Stop very slow movement
            if (
                Mathf.Abs(throttle) < 0.01f &&
                Mathf.Abs(Speed) < 0.15f
            )
            {
                Speed = 0f;
            }

            // ==============================
            // STEERING
            // ==============================

            // Steering gets stronger as speed increases
            float steerAuth = Mathf.Clamp01(
                Mathf.Abs(Speed) /
                (MaxSpeed * 0.35f)
            );

            float yaw =
                steer *
                TurnRate *
                steerAuth *
                Mathf.Sign(
                    Speed == 0f ? 1f : Speed
                ) *
                dt;

            transform.Rotate(
                Vector3.up,
                yaw,
                Space.World
            );

            // ==============================
            // POSITION / MOVEMENT
            // ==============================

            // Move along the flat heading so waves don't push us up/down
            Vector3 heading = transform.forward;
            heading.y = 0f;
            heading.Normalize();

            Vector3 pos =
                transform.position +
                heading * Speed * dt;

            // ==============================
            // OCEAN WAVES
            // ==============================

            float t = Time.time;

            // Move boat up/down with the waves
            pos.y = WaveHeightAt(pos.x, pos.z, t);

            transform.position = pos;

            // Get ocean surface normal
            Vector3 n = WaveNormalAt(pos.x, pos.z, t);

            // Bank boat while steering
            _bank = Mathf.Lerp(
                _bank,
                -steer * 9f * steerAuth,
                dt * 3.5f
            );

            // Align boat with wave surface
            Quaternion level =
                Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(
                        heading,
                        n
                    ).normalized,
                    n
                );

            transform.rotation =
                Quaternion.Slerp(
                    transform.rotation,
                    level *
                    Quaternion.Euler(
                        0f,
                        0f,
                        _bank
                    ),
                    dt * 5f
                );

            // ==============================
            // WAKE AND BOW SPRAY
            // ==============================

            if (_wake == null || _spray == null)
                return;

            // Wake intensity increases with speed
            var em = _wake.emission;

            em.rateOverTime =
                Mathf.Abs(Speed) > 0.6f
                    ? Mathf.Lerp(
                        0f,
                        70f,
                        Speed01
                    )
                    : 0f;

            // Bow spray appears at higher speeds
            var em2 = _spray.emission;

            em2.rateOverTime =
                Speed01 > 0.55f
                    ? Mathf.Lerp(
                        0f,
                        40f,
                        (Speed01 - 0.55f) / 0.45f
                    )
                    : 0f;
        }

        // ==============================
        // OTHER HELPERS
        // ==============================

        /// <summary>
        /// A point ahead of the bow where
        /// the fishing line lands.
        /// </summary>
        public Vector3 CastPoint(float distance)
        {
            return transform.position +
                   transform.forward * distance +
                   Vector3.up * 0.2f;
        }
    }
}
