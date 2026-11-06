using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Two-state fishing loop.
/// INACTIVE does nothing until the Fishing Button is pressed.
/// FISHING runs the cast loop until the Fishing Key returns directly to INACTIVE.
/// Movement stays restricted, and other UI is locked, for the whole FISHING state.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Fishing/Fishing System")]
public class FishingSystem : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("INACTIVE -> FISHING.")]
    [SerializeField] InputAction fishingButton = CreateButton("FishingButton", "<Keyboard>/f", "<Gamepad>/buttonWest");

    [Tooltip("FISHING -> INACTIVE. This is the only way out of FISHING.")]
    [SerializeField] InputAction fishingKey = CreateButton("FishingKey", "<Keyboard>/f", "<Gamepad>/buttonEast");

    [Tooltip("Mouse delta used to aim the rod.")]
    [SerializeField] InputAction rodLook = CreateValue("RodLook", "Vector2", "<Mouse>/delta");

    [SerializeField] InputAction pointerPosition = CreateValue("FishingPointer", "Vector2", "<Mouse>/position");

    [Tooltip("Held input forwarded to the fishing UI and used to drive the reel animation.")]
    [SerializeField] InputAction fishingConfirm = CreateButton("FishingConfirm", "<Mouse>/leftButton", "<Gamepad>/buttonSouth");

    [Header("References")]
    [SerializeField] GameObject fishingUiRoot;
    [SerializeField] MonoBehaviour environmentController;
    [SerializeField] Transform fishingRod;
    [SerializeField] Animator animator;

    [Tooltip("Disabled, or sent SetMovementRestricted, while FISHING.")]
    [SerializeField] MonoBehaviour[] movementBehaviours;

    [Tooltip("Other UI behaviours disabled until FISHING transitions out.")]
    [SerializeField] MonoBehaviour[] otherUiBehaviours;

    [Header("Rod")]
    [SerializeField] float rodSensitivity = 0.08f;
    [SerializeField] Vector2 pitchLimits = new Vector2(-35f, 35f);
    [SerializeField] Vector2 yawLimits = new Vector2(-75f, 75f);

    [Header("Animation Parameters")]
    [SerializeField] string castTrigger = "Cast";
    [SerializeField] string fishingBool = "Fishing";
    [SerializeField] string reelBool = "Reel";
    [SerializeField] string rodYawFloat = "RodYaw";
    [SerializeField] string rodPitchFloat = "RodPitch";

    [Header("Timers")]
    [Tooltip("Seconds between catch checks while FISHING.")]
    [Min(0.01f)]
    [SerializeField] float biteInterval = 5f;

    [Tooltip("Chance that a catch check succeeds. 1 always catches when the bite timer elapses.")]
    [Range(0f, 1f)]
    [SerializeField] float catchChance = 1f;

    [Header("Catch Table")]
    [SerializeField] CatchDefinition[] catchTable = Array.Empty<CatchDefinition>();

    [Header("Events")]
    [SerializeField] UnityEvent onFishingStarted;
    [SerializeField] UnityEvent onFishingEnded;
    [SerializeField] UnityEvent onCatchRelayed;

    readonly List<MonoBehaviour> _disabledWhileFishing = new List<MonoBehaviour>();

    IFishingUi _fishingUi;
    IEnvironmentController _environment;
    Quaternion _rodRestRotation = Quaternion.identity;
    Vector2 _rodAngles;
    bool _exiting;
    bool _warnedEmptyCatchTable;

    int _castHash;
    int _fishingHash;
    int _reelHash;
    int _rodYawHash;
    int _rodPitchHash;
    bool _hasCastTrigger;
    bool _hasFishingBool;
    bool _hasReelBool;
    bool _hasRodYaw;
    bool _hasRodPitch;
    bool _sentFishingValue;
    bool _hasSentFishingValue;
    bool _sentReelValue;
    bool _hasSentReelValue;
    bool _hasSentRodYaw;
    bool _hasSentRodPitch;
    float _sentRodYaw;
    float _sentRodPitch;

    public FishingState CurrentState { get; private set; } = FishingState.Inactive;
    public bool IsFishing => CurrentState == FishingState.Fishing;

    /// <summary>
    /// False for the entire FISHING state, including while it is transitioning out.
    /// Other UI must check this before changing UI state.
    /// </summary>
    public bool CanTransitionToOtherUi => !_exiting && CurrentState != FishingState.Fishing;

    public float SessionTime { get; private set; }
    public float BiteTimer { get; private set; }
    public FishingFrameInput LatestInput { get; private set; }
    public CatchInfo LastCatch { get; private set; }
    public bool HasCatch { get; private set; }

    public event Action<FishingState> StateChanged;
    public event Action<CatchInfo> CatchRelayed;

    void Awake()
    {
        ResolveFishingUi();
        ResolveEnvironment();
        CacheAnimatorParameters();

        if (fishingRod != null)
            _rodRestRotation = fishingRod.localRotation;
    }

    void OnEnable()
    {
        EnableAction(fishingButton);
        EnableAction(fishingKey);
        EnableAction(rodLook);
        EnableAction(pointerPosition);
        EnableAction(fishingConfirm);
    }

    void OnDisable()
    {
        if (CurrentState == FishingState.Fishing)
            ExitFishing();

        DisableAction(fishingButton);
        DisableAction(fishingKey);
        DisableAction(rodLook);
        DisableAction(pointerPosition);
        DisableAction(fishingConfirm);
    }

    void Update()
    {
        switch (CurrentState)
        {
            case FishingState.Inactive:
                TickInactive();
                break;
            case FishingState.Fishing:
                TickFishing();
                break;
        }
    }

    void TickInactive()
    {
        if (fishingButton != null && fishingButton.WasPressedThisFrame())
            EnterFishing();
    }

    void TickFishing()
    {
        if (fishingKey != null && fishingKey.WasPressedThisFrame())
        {
            ExitFishing();
            return;
        }

        FishingFrameInput input = ReadFrameInput();
        LatestInput = input;

        MoveRod(input);
        PlayAnimations(input);
        _fishingUi?.RegisterInput(input);
        UpdateTimers(Time.deltaTime);

        if (!DetermineWhetherCaught())
            return;

        CatchInfo catchInfo = DetermineWhatWasCaught();
        RelayCatch(catchInfo);
    }

    void EnterFishing()
    {
        if (CurrentState != FishingState.Inactive)
            return;

        CurrentState = FishingState.Fishing;
        SessionTime = 0f;
        BiteTimer = 0f;
        HasCatch = false;
        _rodAngles = Vector2.zero;
        LatestInput = default;

        if (fishingRod != null)
            _rodRestRotation = fishingRod.localRotation;

        if (fishingUiRoot != null)
            fishingUiRoot.SetActive(true);

        _fishingUi?.Activate();

        if (CurrentState != FishingState.Fishing)
            return;

        RestrictMovementAndUi();
        PlayCastAnimation();
        onFishingStarted?.Invoke();
        StateChanged?.Invoke(CurrentState);
    }

    void ExitFishing()
    {
        if (CurrentState != FishingState.Fishing || _exiting)
            return;

        _exiting = true;

        SetFishingAnimation(false);
        if (_hasCastTrigger && animator != null)
            animator.ResetTrigger(_castHash);

        if (fishingRod != null)
            fishingRod.localRotation = _rodRestRotation;

        _fishingUi?.Deactivate();
        if (fishingUiRoot != null)
            fishingUiRoot.SetActive(false);

        RestoreRestrictedBehaviours();
        LatestInput = default;

        CurrentState = FishingState.Inactive;
        _exiting = false;

        onFishingEnded?.Invoke();
        StateChanged?.Invoke(CurrentState);
    }

    FishingFrameInput ReadFrameInput()
    {
        return new FishingFrameInput
        {
            PointerDelta = rodLook != null ? rodLook.ReadValue<Vector2>() : Vector2.zero,
            PointerPosition = pointerPosition != null ? pointerPosition.ReadValue<Vector2>() : Vector2.zero,
            ConfirmPressed = fishingConfirm != null && fishingConfirm.WasPressedThisFrame(),
            ConfirmHeld = fishingConfirm != null && fishingConfirm.IsPressed()
        };
    }

    void MoveRod(FishingFrameInput input)
    {
        if (fishingRod == null)
            return;

        _rodAngles.x = Mathf.Clamp(
            _rodAngles.x - input.PointerDelta.y * rodSensitivity,
            pitchLimits.x,
            pitchLimits.y);
        _rodAngles.y = Mathf.Clamp(
            _rodAngles.y + input.PointerDelta.x * rodSensitivity,
            yawLimits.x,
            yawLimits.y);

        fishingRod.localRotation = _rodRestRotation * Quaternion.Euler(_rodAngles.x, _rodAngles.y, 0f);
    }

    void PlayCastAnimation()
    {
        if (animator == null)
            return;

        if (_hasCastTrigger)
            animator.SetTrigger(_castHash);

        SetFishingAnimation(true);
    }

    void PlayAnimations(FishingFrameInput input)
    {
        if (animator == null)
            return;

        SetFishingAnimation(true);
        SetReelAnimation(input.ConfirmHeld);

        if (_hasRodYaw)
        {
            float yaw = Mathf.InverseLerp(yawLimits.x, yawLimits.y, _rodAngles.y);
            if (!_hasSentRodYaw || _sentRodYaw != yaw)
            {
                _hasSentRodYaw = true;
                _sentRodYaw = yaw;
                animator.SetFloat(_rodYawHash, yaw);
            }
        }

        if (_hasRodPitch)
        {
            float pitch = Mathf.InverseLerp(pitchLimits.x, pitchLimits.y, _rodAngles.x);
            if (!_hasSentRodPitch || _sentRodPitch != pitch)
            {
                _hasSentRodPitch = true;
                _sentRodPitch = pitch;
                animator.SetFloat(_rodPitchHash, pitch);
            }
        }
    }

    void SetFishingAnimation(bool fishing)
    {
        if (animator == null || !_hasFishingBool)
            return;

        if (_hasSentFishingValue && _sentFishingValue == fishing)
            return;

        _hasSentFishingValue = true;
        _sentFishingValue = fishing;
        animator.SetBool(_fishingHash, fishing);
    }

    void SetReelAnimation(bool reeling)
    {
        if (animator == null || !_hasReelBool)
            return;

        if (_hasSentReelValue && _sentReelValue == reeling)
            return;

        _hasSentReelValue = true;
        _sentReelValue = reeling;
        animator.SetBool(_reelHash, reeling);
    }

    void UpdateTimers(float deltaTime)
    {
        SessionTime += deltaTime;
        BiteTimer += deltaTime;
    }

    bool DetermineWhetherCaught()
    {
        if (BiteTimer < biteInterval)
            return false;

        BiteTimer = 0f;
        return UnityEngine.Random.value <= catchChance;
    }

    CatchInfo DetermineWhatWasCaught()
    {
        float totalWeight = 0f;
        if (catchTable != null)
        {
            for (int i = 0; i < catchTable.Length; i++)
            {
                CatchDefinition entry = catchTable[i];
                if (entry != null && entry.weight > 0f)
                    totalWeight += entry.weight;
            }
        }

        if (totalWeight <= 0f)
        {
            if (!_warnedEmptyCatchTable)
            {
                _warnedEmptyCatchTable = true;
                Debug.LogWarning("FishingSystem has no weighted catch table entries. Relaying an unspecified catch.", this);
            }

            return new CatchInfo
            {
                Kind = CatchKind.Item,
                Id = "unspecified",
                DisplayName = "Unspecified"
            };
        }

        float roll = UnityEngine.Random.value * totalWeight;
        float cursor = 0f;
        CatchDefinition lastValid = null;

        for (int i = 0; i < catchTable.Length; i++)
        {
            CatchDefinition entry = catchTable[i];
            if (entry == null || entry.weight <= 0f)
                continue;

            lastValid = entry;
            cursor += entry.weight;
            if (roll <= cursor)
                return ToCatchInfo(entry);
        }

        return ToCatchInfo(lastValid);
    }

    void RelayCatch(CatchInfo catchInfo)
    {
        LastCatch = catchInfo;
        HasCatch = true;

        _fishingUi?.PresentCatch(catchInfo);
        _environment?.RelayCatch(catchInfo);
        onCatchRelayed?.Invoke();
        CatchRelayed?.Invoke(catchInfo);
    }

    void RestrictMovementAndUi()
    {
        RestrictBehaviours(movementBehaviours, true);
        RestrictBehaviours(otherUiBehaviours, false);
    }

    void RestrictBehaviours(MonoBehaviour[] behaviours, bool notifyMovement)
    {
        if (behaviours == null)
            return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == this)
                continue;

            if (notifyMovement && behaviour is IMovementRestrictable restrictable)
            {
                restrictable.SetMovementRestricted(true);
                continue;
            }

            if (!behaviour.enabled)
                continue;

            behaviour.enabled = false;
            _disabledWhileFishing.Add(behaviour);
        }
    }

    void RestoreRestrictedBehaviours()
    {
        if (movementBehaviours != null)
        {
            for (int i = 0; i < movementBehaviours.Length; i++)
            {
                if (movementBehaviours[i] is IMovementRestrictable restrictable)
                    restrictable.SetMovementRestricted(false);
            }
        }

        for (int i = 0; i < _disabledWhileFishing.Count; i++)
        {
            if (_disabledWhileFishing[i] != null)
                _disabledWhileFishing[i].enabled = true;
        }

        _disabledWhileFishing.Clear();
    }

    void ResolveFishingUi()
    {
        if (fishingUiRoot == null)
            return;

        MonoBehaviour[] behaviours = fishingUiRoot.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IFishingUi fishingUi)
            {
                _fishingUi = fishingUi;
                return;
            }
        }
    }

    void ResolveEnvironment()
    {
        if (environmentController == null)
            return;

        _environment = environmentController as IEnvironmentController;
        if (_environment == null)
        {
            Debug.LogWarning(
                $"{nameof(FishingSystem)} on {name}: {environmentController.GetType().Name} does not implement {nameof(IEnvironmentController)}.",
                this);
        }
    }

    void CacheAnimatorParameters()
    {
        if (animator == null)
            return;

        _castHash = Animator.StringToHash(castTrigger);
        _fishingHash = Animator.StringToHash(fishingBool);
        _reelHash = Animator.StringToHash(reelBool);
        _rodYawHash = Animator.StringToHash(rodYawFloat);
        _rodPitchHash = Animator.StringToHash(rodPitchFloat);

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.name == castTrigger && parameter.type == AnimatorControllerParameterType.Trigger)
                _hasCastTrigger = true;
            else if (parameter.name == fishingBool && parameter.type == AnimatorControllerParameterType.Bool)
                _hasFishingBool = true;
            else if (parameter.name == reelBool && parameter.type == AnimatorControllerParameterType.Bool)
                _hasReelBool = true;
            else if (parameter.name == rodYawFloat && parameter.type == AnimatorControllerParameterType.Float)
                _hasRodYaw = true;
            else if (parameter.name == rodPitchFloat && parameter.type == AnimatorControllerParameterType.Float)
                _hasRodPitch = true;
        }
    }

    static CatchInfo ToCatchInfo(CatchDefinition entry)
    {
        return new CatchInfo
        {
            Kind = entry.kind,
            Id = entry.id,
            DisplayName = entry.displayName
        };
    }

    static void EnableAction(InputAction action)
    {
        if (action != null && !action.enabled)
            action.Enable();
    }

    static void DisableAction(InputAction action)
    {
        if (action != null && action.enabled)
            action.Disable();
    }

    static InputAction CreateButton(string actionName, params string[] bindings)
    {
        InputAction action = new InputAction(actionName, InputActionType.Button);
        for (int i = 0; i < bindings.Length; i++)
            action.AddBinding(bindings[i]);
        return action;
    }

    static InputAction CreateValue(string actionName, string controlType, string binding)
    {
        return new InputAction(actionName, InputActionType.Value, binding, null, null, controlType);
    }
}

public enum FishingState
{
    Inactive,
    Fishing
}

public enum CatchKind
{
    Item,
    Entity
}

[Serializable]
public struct CatchInfo
{
    public CatchKind Kind;
    public string Id;
    public string DisplayName;
}

[Serializable]
public class CatchDefinition
{
    public CatchKind kind = CatchKind.Item;
    public string id = "catch";
    public string displayName = "Catch";
    [Min(0f)] public float weight = 1f;
}

public struct FishingFrameInput
{
    public Vector2 PointerDelta;
    public Vector2 PointerPosition;
    public bool ConfirmPressed;
    public bool ConfirmHeld;
}

public interface IFishingUi
{
    void Activate();
    void Deactivate();
    void RegisterInput(FishingFrameInput input);
    void PresentCatch(CatchInfo catchInfo);
}

public interface IEnvironmentController
{
    void RelayCatch(CatchInfo catchInfo);
}

public interface IMovementRestrictable
{
    void SetMovementRestricted(bool restricted);
}
