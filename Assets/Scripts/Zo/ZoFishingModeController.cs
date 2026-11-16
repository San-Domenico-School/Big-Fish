using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BigFish
{
    /// <summary>
    /// Zo's fishing mode, layered on top of Sufian's FishingSystem.
    ///
    /// FishingSystem owns: F = enter fishing, Esc = leave, the bite timer and the catch table.
    /// This script adds:
    ///   - boat anchored (WASD does nothing) while fishing
    ///   - camera flies to the bow of the boat, looking forward, and the mouse looks around
    ///   - voxel rod in first person
    ///   - SPACE casts (rod swing + bobber flies out), SPACE again reels in
    ///   - when FishingSystem reports a catch while the line is in the water, it's a bite
    ///     and the catch minigame starts (swappable - see ZoCatchMinigame)
    /// </summary>
    [DisallowMultipleComponent]
    public class ZoFishingModeController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] FishingSystem fishingSystem;
        [SerializeField] BoatSpawner boatSpawner;
        [SerializeField] Camera fishingCamera;
        [SerializeField] ZoVoxelFishingRod rod;

        [Tooltip("Any ZoCatchMinigame. Swap this to use a different catch UI. Empty = bites always catch.")]
        [SerializeField] ZoCatchMinigame catchMinigame;

        [Header("Input")]
        [Tooltip("Cast / reel in / minigame action. Left click also works (FishingSystem's confirm input).")]
        [SerializeField] InputAction castAction = new InputAction("Cast", InputActionType.Button, "<Keyboard>/space");

        [Header("Fishing Camera (local to the boat, +Z = bow / front)")]
        [SerializeField] Vector3 cameraLocalPosition = new Vector3(0f, 3.2f, 4.6f);

        [Tooltip("0 = looking forward off the bow.")]
        [SerializeField] float cameraYaw = 0f;

        [Tooltip("Positive tilts the camera down.")]
        [SerializeField] float cameraPitch = 8f;

        [SerializeField] float fishingFieldOfView = 70f;

        [Min(0.01f)]
        [SerializeField] float transitionDuration = 1.1f;

        [Header("Look Around")]
        [SerializeField] float lookSensitivity = 0.12f;

        [Tooltip("How far left/right you can turn. 180 or more = all the way around.")]
        [SerializeField] float yawLimit = 180f;

        [Tooltip("x = furthest up (negative), y = furthest down.")]
        [SerializeField] Vector2 pitchLimits = new Vector2(-50f, 60f);

        [Header("Cast")]
        [SerializeField] float castDistance = 14f;
        [SerializeField] float castFlightTime = 0.7f;
        [SerializeField] float castArcHeight = 3f;

        [Tooltip("Bites that come in sooner than this after the bobber lands wait until this much time has passed.")]
        [SerializeField] float minBiteDelay = 1.5f;

        [Header("Reel In")]
        [SerializeField] float reelDuration = 0.9f;

        [Header("Line & Bobber")]
        [SerializeField] float lineSag = 0.8f;
        [SerializeField] float lineWidth = 0.015f;
        [SerializeField] Color lineColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] float bobberSize = 0.3f;

        [Header("Messages")]
        [SerializeField] float messageSeconds = 2.5f;

        enum Mode { Following, Entering, Idle, Casting, LineOut, Hooked, Reeling, Exiting }

        // Cast animation timing (seconds)
        const float SwingBackTime = 0.22f;
        const float WhipTime = 0.14f;
        const float SettleTime = 0.3f;
        const int LineSegments = 20;

        Mode _mode = Mode.Following;
        float _stateTime;
        Coroutine _transition;

        float _lookYaw;
        float _lookPitch;
        float _defaultFov;

        LineRenderer _line;
        Transform _bobber;
        GameObject _fishVisual;

        Vector3 _bobberTargetLocal;   // boat-local so it stays put relative to the boat
        Vector3 _flightStart;
        Vector3 _reelStart;
        bool _released;
        float _flightT;
        float _landDip;

        bool _pendingBite;
        CatchInfo _pendingFish;
        CatchInfo _hookedFish;
        bool _reelingCaughtFish;

        string _message;
        float _messageUntil;

        BoatController Boat => boatSpawner != null ? boatSpawner.Boat : null;

        public bool IsLineOut => _mode == Mode.LineOut;

        // ==============================
        // SETUP
        // ==============================

        void Awake()
        {
            if (fishingCamera == null)
                fishingCamera = Camera.main;
            if (fishingSystem == null)
                fishingSystem = FindAnyObjectByType<FishingSystem>();
            if (boatSpawner == null)
                boatSpawner = FindAnyObjectByType<BoatSpawner>();

            if (fishingCamera != null)
                _defaultFov = fishingCamera.fieldOfView;

            if (rod != null)
                rod.Build();

            BuildLineAndBobber();
            SetFishingVisualsVisible(false);
        }

        void OnEnable()
        {
            castAction?.Enable();

            if (fishingSystem != null)
            {
                fishingSystem.StateChanged += HandleStateChanged;
                fishingSystem.CatchRelayed += HandleCatchRelayed;
            }

            if (catchMinigame != null)
                catchMinigame.Finished += HandleMinigameFinished;
        }

        void OnDisable()
        {
            castAction?.Disable();

            if (fishingSystem != null)
            {
                fishingSystem.StateChanged -= HandleStateChanged;
                fishingSystem.CatchRelayed -= HandleCatchRelayed;
            }

            if (catchMinigame != null)
                catchMinigame.Finished -= HandleMinigameFinished;
        }

        void BuildLineAndBobber()
        {
            var lineGo = new GameObject("FishingLine");
            lineGo.transform.SetParent(transform, false);

            _line = lineGo.AddComponent<LineRenderer>();
            _line.positionCount = LineSegments;
            _line.useWorldSpace = true;
            _line.widthMultiplier = lineWidth;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.material = BoatController.MakeParticleMaterial(lineColor);
            _line.startColor = lineColor;
            _line.endColor = lineColor;

            // Voxel bobber: red top, white bottom, little stick
            _bobber = new GameObject("Bobber").transform;
            _bobber.SetParent(transform, false);
            float s = bobberSize;
            ZoVoxels.Cube(_bobber, new Vector3(0f, s * 0.25f, 0f), new Vector3(s, s * 0.5f, s), ZoVoxels.Mat(new Color(0.9f, 0.15f, 0.1f)));
            ZoVoxels.Cube(_bobber, new Vector3(0f, -s * 0.25f, 0f), new Vector3(s, s * 0.5f, s), ZoVoxels.Mat(Color.white));
            ZoVoxels.Cube(_bobber, new Vector3(0f, s * 0.7f, 0f), new Vector3(s * 0.25f, s * 0.4f, s * 0.25f), ZoVoxels.Mat(new Color(0.15f, 0.15f, 0.15f)));

            // Voxel fish that dangles under the bobber when you reel one in
            _fishVisual = new GameObject("CaughtFish");
            _fishVisual.transform.SetParent(_bobber, false);
            _fishVisual.transform.localPosition = new Vector3(0f, -0.75f, 0f);
            Material fishMat = ZoVoxels.Mat(new Color(0.2f, 0.55f, 0.95f));
            Material finMat = ZoVoxels.Mat(new Color(1f, 0.8f, 0.15f));
            ZoVoxels.Cube(_fishVisual.transform, new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.5f, 0.25f), fishMat);
            ZoVoxels.Cube(_fishVisual.transform, new Vector3(0f, -0.32f, 0f), new Vector3(0.08f, 0.14f, 0.3f), finMat);
            ZoVoxels.Cube(_fishVisual.transform, new Vector3(0.07f, 0.15f, 0.06f), Vector3.one * 0.04f, ZoVoxels.Mat(Color.black));
            _fishVisual.SetActive(false);
        }

        // ==============================
        // FISHING SYSTEM EVENTS
        // ==============================

        void HandleStateChanged(FishingState state)
        {
            if (state == FishingState.Fishing)
                BeginFishingMode();
            else
                EndFishingMode();
        }

        void HandleCatchRelayed(CatchInfo info)
        {
            // FishingSystem rolls catches on its own timer; we only count them as a bite
            // while the bobber is actually in the water.
            if (_mode != Mode.LineOut || _pendingBite)
                return;

            _pendingBite = true;
            _pendingFish = info;
        }

        void HandleMinigameFinished(bool caught)
        {
            if (_mode != Mode.Hooked)
                return;

            string custom = catchMinigame != null ? catchMinigame.ResultMessage : null;
            bool minigameShowsCard = caught && catchMinigame != null && catchMinigame.ShowsOwnCatchCard;

            if (!string.IsNullOrEmpty(custom))
                ShowMessage(custom);
            else if (!minigameShowsCard)
                ShowMessage(caught ? $"You caught: {_hookedFish.DisplayName}!" : "It got away...");
            if (caught)
                Debug.Log($"[ZoFishing] Caught {_hookedFish.DisplayName} ({_hookedFish.Id})", this);

            StartReel(caught);
        }

        void BeginFishingMode()
        {
            if (Boat != null)
                Boat.Anchored = true; // WASD no longer moves the boat

            if (boatSpawner != null)
                boatSpawner.enabled = false; // take the camera away from the follow cam

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _lookYaw = 0f;
            _lookPitch = 0f;
            _pendingBite = false;

            StartTransition(Mode.Entering);
        }

        void EndFishingMode()
        {
            if (catchMinigame != null)
                catchMinigame.Cancel();

            _pendingBite = false;

            if (Boat != null)
                Boat.Anchored = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SetFishingVisualsVisible(false);
            StartTransition(Mode.Exiting);
        }

        // ==============================
        // CAMERA TRANSITIONS
        // ==============================

        void StartTransition(Mode mode)
        {
            if (_transition != null)
                StopCoroutine(_transition);

            if (fishingCamera == null)
                return;

            _transition = StartCoroutine(Transition(mode));
        }

        IEnumerator Transition(Mode mode)
        {
            SetMode(mode);

            Transform cam = fishingCamera.transform;
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;
            float fromFov = fishingCamera.fieldOfView;
            bool entering = mode == Mode.Entering;
            float toFov = entering ? fishingFieldOfView : _defaultFov;

            for (float t = 0f; t < 1f; t += Time.deltaTime / transitionDuration)
            {
                // Target recomputed every frame because the boat bobs on the waves
                Vector3 toPos;
                Quaternion toRot;
                if (entering)
                    GetFishingPose(out toPos, out toRot);
                else
                    GetFollowPose(out toPos, out toRot);

                float e = Mathf.SmoothStep(0f, 1f, t);
                cam.position = Vector3.Lerp(fromPos, toPos, e);
                cam.rotation = Quaternion.Slerp(fromRot, toRot, e);
                fishingCamera.fieldOfView = Mathf.Lerp(fromFov, toFov, e);
                yield return null;
            }

            fishingCamera.fieldOfView = toFov;
            _transition = null;

            if (entering)
            {
                SetFishingVisualsVisible(true);
                SetMode(Mode.Idle);
            }
            else
            {
                SetMode(Mode.Following);
                if (boatSpawner != null)
                    boatSpawner.enabled = true; // hand the camera back to the follow cam
            }
        }

        void GetFishingPose(out Vector3 pos, out Quaternion rot)
        {
            Transform boat = Boat != null ? Boat.transform : null;
            if (boat == null)
            {
                pos = fishingCamera.transform.position;
                rot = fishingCamera.transform.rotation;
                return;
            }

            // Flat heading so wave roll doesn't make you seasick
            Quaternion heading = Quaternion.Euler(0f, boat.eulerAngles.y, 0f);
            pos = boat.position + heading * cameraLocalPosition;
            rot = heading * Quaternion.Euler(cameraPitch + _lookPitch, cameraYaw + _lookYaw, 0f);
        }

        void GetFollowPose(out Vector3 pos, out Quaternion rot)
        {
            Transform boat = Boat != null ? Boat.transform : null;
            if (boat == null || boatSpawner == null)
            {
                pos = fishingCamera.transform.position;
                rot = fishingCamera.transform.rotation;
                return;
            }

            // Same framing BoatSpawner uses for its follow cam
            Quaternion heading = Quaternion.Euler(0f, boat.eulerAngles.y, 0f);
            pos = boat.position + heading * boatSpawner.CameraOffset;
            rot = Quaternion.LookRotation(boat.position + Vector3.up * 2f - pos, Vector3.up);
        }

        // ==============================
        // MAIN LOOP
        // ==============================

        void LateUpdate()
        {
            if (_mode == Mode.Following || _mode == Mode.Entering || _mode == Mode.Exiting)
                return;

            float dt = Time.deltaTime;
            _stateTime += dt;

            FishingFrameInput fsInput = fishingSystem != null ? fishingSystem.LatestInput : default;
            bool pressed = (castAction != null && castAction.WasPressedThisFrame()) || fsInput.ConfirmPressed;
            bool held = (castAction != null && castAction.IsPressed()) || fsInput.ConfirmHeld;

            UpdateLook(fsInput.PointerDelta);
            GetFishingPose(out Vector3 camPos, out Quaternion camRot);
            fishingCamera.transform.SetPositionAndRotation(camPos, camRot);

            switch (_mode)
            {
                case Mode.Idle:
                    TickIdle(pressed);
                    break;
                case Mode.Casting:
                    TickCasting(dt);
                    break;
                case Mode.LineOut:
                    TickLineOut(dt, pressed);
                    break;
                case Mode.Hooked:
                    TickHooked(dt, pressed, held);
                    break;
                case Mode.Reeling:
                    TickReeling(dt);
                    break;
            }

            UpdateLine();
        }

        void UpdateLook(Vector2 mouseDelta)
        {
            _lookYaw += mouseDelta.x * lookSensitivity;
            _lookPitch = Mathf.Clamp(_lookPitch - mouseDelta.y * lookSensitivity, pitchLimits.x, pitchLimits.y);

            if (yawLimit >= 180f)
                _lookYaw = Mathf.Repeat(_lookYaw + 180f, 360f) - 180f;
            else
                _lookYaw = Mathf.Clamp(_lookYaw, -yawLimit, yawLimit);
        }

        void SetMode(Mode mode)
        {
            _mode = mode;
            _stateTime = 0f;
        }

        // ---------- IDLE: rod ready, bobber dangling ----------

        void TickIdle(bool pressed)
        {
            rod?.SetSwing(Mathf.Sin(Time.time * 1.5f) * 1.5f);
            _bobber.position = TipPosition() + Vector3.down * 0.35f;

            if (pressed)
                StartCast();
        }

        // ---------- CASTING: swing back, whip forward, bobber flies ----------

        void StartCast()
        {
            Transform cam = fishingCamera.transform;
            Vector3 flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            flatForward.Normalize();

            Vector3 target = cam.position + flatForward * castDistance;
            _bobberTargetLocal = Boat != null ? Boat.transform.InverseTransformPoint(target) : target;

            _released = false;
            _flightT = 0f;
            _fishVisual.SetActive(false);
            SetMode(Mode.Casting);
        }

        void TickCasting(float dt)
        {
            float t = _stateTime;
            float swing;

            if (t < SwingBackTime)
                swing = Mathf.Lerp(0f, -50f, Mathf.SmoothStep(0f, 1f, t / SwingBackTime));
            else if (t < SwingBackTime + WhipTime)
                swing = Mathf.Lerp(-50f, 25f, (t - SwingBackTime) / WhipTime);
            else
                swing = Mathf.Lerp(25f, 0f, Mathf.SmoothStep(0f, 1f, (t - SwingBackTime - WhipTime) / SettleTime));

            rod?.SetSwing(swing);

            if (!_released && t >= SwingBackTime + WhipTime * 0.5f)
            {
                _released = true;
                _flightStart = _bobber.position;
            }

            if (!_released)
            {
                _bobber.position = TipPosition() + Vector3.down * 0.35f;
                return;
            }

            _flightT += dt / Mathf.Max(0.01f, castFlightTime);
            float f = Mathf.Clamp01(_flightT);
            _bobber.position = Vector3.Lerp(_flightStart, BobberWaterPosition(), f)
                               + Vector3.up * Mathf.Sin(f * Mathf.PI) * castArcHeight;

            if (f >= 1f)
            {
                _landDip = 1f;
                _pendingBite = false;
                SetMode(Mode.LineOut);
            }
        }

        // ---------- LINE OUT: waiting for a bite ----------

        void TickLineOut(float dt, bool pressed)
        {
            rod?.SetSwing(Mathf.Sin(Time.time * 1.2f) * 1f);

            float bob = Mathf.Sin(Time.time * 2.2f) * 0.06f;
            if (_landDip > 0f)
            {
                bob -= Mathf.Sin((1f - _landDip) * Mathf.PI) * 0.3f * _landDip;
                _landDip -= dt * 1.5f;
            }
            _bobber.position = BobberWaterPosition() + Vector3.up * bob;

            if (pressed)
            {
                StartReel(false); // reeled in early, nothing on the hook
                return;
            }

            if (_pendingBite && _stateTime >= minBiteDelay)
                Hook();
        }

        void Hook()
        {
            _pendingBite = false;
            _hookedFish = _pendingFish;
            SetMode(Mode.Hooked);

            if (catchMinigame != null)
                catchMinigame.Begin(_hookedFish);
            else
                HandleMinigameFinished(true);
        }

        // ---------- HOOKED: minigame running ----------

        void TickHooked(float dt, bool pressed, bool held)
        {
            // Fish tugging: bobber yanked under, rod shaking
            float tug = Mathf.Sin(_stateTime * 14f);
            _bobber.position = BobberWaterPosition()
                               + Vector3.down * (0.25f + tug * 0.12f)
                               + Vector3.right * Mathf.Sin(_stateTime * 5f) * 0.3f;

            rod?.SetSwing(-12f + Mathf.Sin(_stateTime * 25f) * 3f, Mathf.Sin(_stateTime * 9f) * 4f);
            if (held)
                rod?.SpinReel(720f * dt);

            if (catchMinigame != null)
                catchMinigame.Tick(dt, pressed, held);
        }

        // ---------- REELING: bobber comes back to the rod ----------

        void StartReel(bool caughtFish)
        {
            _reelingCaughtFish = caughtFish;
            _reelStart = _bobber.position;
            _fishVisual.SetActive(caughtFish);
            SetMode(Mode.Reeling);
        }

        void TickReeling(float dt)
        {
            float f = Mathf.Clamp01(_stateTime / Mathf.Max(0.01f, reelDuration));
            float e = Mathf.SmoothStep(0f, 1f, f);

            rod?.SetSwing(-30f * Mathf.Sin(f * Mathf.PI));
            rod?.SpinReel(900f * dt);

            Vector3 hang = TipPosition() + Vector3.down * 0.35f;
            _bobber.position = Vector3.Lerp(_reelStart, hang, e) + Vector3.up * Mathf.Sin(f * Mathf.PI) * 1.5f;

            if (_reelingCaughtFish)
                _fishVisual.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 20f) * 25f);

            if (f >= 1f)
            {
                if (_reelingCaughtFish)
                    StartCoroutine(HideFishAfter(1.2f));
                SetMode(Mode.Idle);
            }
        }

        IEnumerator HideFishAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_mode == Mode.Idle)
                _fishVisual.SetActive(false);
        }

        // ==============================
        // LINE & BOBBER HELPERS
        // ==============================

        Vector3 TipPosition()
        {
            if (rod != null && rod.Tip != null)
                return rod.Tip.position;

            return fishingCamera.transform.TransformPoint(new Vector3(0.2f, 0.4f, 1.8f));
        }

        Vector3 BobberWaterPosition()
        {
            if (Boat == null)
                return _bobberTargetLocal;

            Vector3 p = Boat.transform.TransformPoint(_bobberTargetLocal);
            p.y = Boat.WaterLevel;
            return p;
        }

        void UpdateLine()
        {
            Vector3 tip = TipPosition();
            Vector3 end = _bobber.position + Vector3.up * bobberSize * 0.9f;
            float sag = _mode == Mode.LineOut ? lineSag : 0f;

            for (int i = 0; i < LineSegments; i++)
            {
                float s = i / (float)(LineSegments - 1);
                Vector3 p = Vector3.Lerp(tip, end, s);
                p.y -= Mathf.Sin(s * Mathf.PI) * sag;
                _line.SetPosition(i, p);
            }
        }

        void SetFishingVisualsVisible(bool visible)
        {
            if (rod != null)
                rod.SetVisible(visible);
            if (_line != null)
                _line.gameObject.SetActive(visible);
            if (_bobber != null)
                _bobber.gameObject.SetActive(visible);
            if (!visible && _fishVisual != null)
                _fishVisual.SetActive(false);
        }

        // ==============================
        // ON-SCREEN TEXT
        // ==============================

        void ShowMessage(string text)
        {
            _message = text;
            _messageUntil = Time.time + messageSeconds;
        }

        void OnGUI()
        {
            string hint = null;
            switch (_mode)
            {
                case Mode.Following:
                    if (Boat != null) hint = "Press F to fish";
                    break;
                case Mode.Idle:
                    hint = "SPACE to cast  -  mouse to look around  -  Esc to stop fishing";
                    break;
                case Mode.LineOut:
                    hint = "Waiting for a bite...  SPACE to reel in  -  Esc to stop fishing";
                    break;
            }

            if (hint != null)
                GUI.Label(new Rect(20, 20, 600, 25), hint);

            if (!string.IsNullOrEmpty(_message) && Time.time < _messageUntil)
            {
                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 40), _message, style);
            }
        }
    }
}
