using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Player data owner.
/// IDLE holds money, inventory, rank, and unlocks.
/// PROCESS TRANSACTION, PROCESS CATCH, and PROGRESSION UPDATE are the only states that change that data.
/// Each one returns to IDLE, except a catch that raises a rank or qualifies an unlock, which goes to PROGRESSION UPDATE first.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Player/Player Manager")]
public class PlayerManager : MonoBehaviour
{
    [Header("Starting Data")]
    [SerializeField] int startingMoney;
    [Tooltip("When enabled, purchases may reduce money below zero.")]
    [SerializeField] bool allowDebt;
    [Min(0)]
    [SerializeField] int startingExperience;
    [Min(0)]
    [SerializeField] int inventoryCapacity = 20;
    [Min(0)]
    [Tooltip("Experience used when a catch id is not in the catch reward table.")]
    [SerializeField] int defaultCatchExperience = 10;

    [Header("Definitions")]
    [SerializeField] RankDefinition[] ranks = Array.Empty<RankDefinition>();
    [SerializeField] UnlockDefinition[] unlocks = Array.Empty<UnlockDefinition>();
    [SerializeField] CatchRewardDefinition[] catchRewards = Array.Empty<CatchRewardDefinition>();
    [SerializeField] StartingStack[] startingFish = Array.Empty<StartingStack>();
    [SerializeField] StartingStack[] startingItems = Array.Empty<StartingStack>();
    [SerializeField] StartingUnlock[] startingEquipment = Array.Empty<StartingUnlock>();
    [SerializeField] StartingUnlock[] startingBoatUpgrades = Array.Empty<StartingUnlock>();
    [SerializeField] StartingUnlock[] startingRegions = Array.Empty<StartingUnlock>();

    [Header("References")]
    [Tooltip("Successful catches on this system enter PROCESS CATCH.")]
    [SerializeField] FishingSystem fishingSystem;
    [SerializeField] MonoBehaviour uiManager;
    [SerializeField] MonoBehaviour worldStateManager;
    [SerializeField] MonoBehaviour[] unlockReceivers = Array.Empty<MonoBehaviour>();

    [Header("Events")]
    [SerializeField] UnityEvent onPlayerDataChanged;
    [SerializeField] UnityEvent onTransactionResolved;
    [SerializeField] UnityEvent onCatchProcessed;
    [SerializeField] UnityEvent onProgressionUpdated;

    readonly List<RankDefinition> _ranks = new List<RankDefinition>();
    readonly List<InventoryStack> _fish = new List<InventoryStack>();
    readonly List<InventoryStack> _items = new List<InventoryStack>();
    readonly Dictionary<string, UnlockRecord> _unlocks = new Dictionary<string, UnlockRecord>();
    readonly Dictionary<string, CatchRewardDefinition> _rewardLookup = new Dictionary<string, CatchRewardDefinition>();
    readonly HashSet<string> _appliedTransactionIds = new HashSet<string>();
    readonly HashSet<string> _reportedCatchTokens = new HashSet<string>();
    readonly HashSet<string> _clearances = new HashSet<string>();
    readonly List<IUnlockReceiver> _receivers = new List<IUnlockReceiver>();
    readonly Queue<PendingWork> _pending = new Queue<PendingWork>();

    IPlayerUi _ui;
    IWorldStateManager _world;
    int _money;
    int _experience;
    int _rankIndex;
    int _slotsUsed;
    bool _draining;
    bool _warnedSkippedStartingItems;

    public PlayerManagerState CurrentState { get; private set; } = PlayerManagerState.Idle;
    public bool IsIdle => CurrentState == PlayerManagerState.Idle && !_draining;
    public int Money => _money;
    public int Experience => _experience;
    public int InventoryUsed => _slotsUsed;
    public int InventoryCapacity => inventoryCapacity;
    public int RemainingInventory => Mathf.Max(0, inventoryCapacity - _slotsUsed);
    public string RankId => RankIdAt(_rankIndex);
    public string RankName => RankNameAt(_rankIndex);

    public event Action<PlayerManagerState> StateChanged;
    public event Action<PlayerDataSnapshot> PlayerDataChanged;
    public event Action<TransactionResult> TransactionResolved;
    public event Action<CatchProcessResult> CatchProcessed;
    public event Action<ProgressionResult> ProgressionUpdated;

    void Awake()
    {
        _money = startingMoney;
        if (!allowDebt && _money < 0)
            _money = 0;

        _experience = Mathf.Max(0, startingExperience);
        BuildRanks();
        BuildRewardLookup();
        AddStartingUnlocks();
        AddStartingInventory(startingFish, CatchKind.Entity, _fish);
        AddStartingInventory(startingItems, CatchKind.Item, _items);
        RecalculateSlots();
        GrantQualifiedUnlocks(notify: false);
        ResolveCollaborators();
    }

    void Start()
    {
        PublishOwnedUnlocks();
    }

    void OnEnable()
    {
        if (fishingSystem != null)
            fishingSystem.CatchRelayed += HandleFishingCatch;
    }

    void OnDisable()
    {
        if (fishingSystem != null)
            fishingSystem.CatchRelayed -= HandleFishingCatch;
    }

    void Update()
    {
        if (CurrentState == PlayerManagerState.Idle && !_draining && _pending.Count > 0)
            Drain();
    }

    /// <summary>
    /// IDLE: give the UI Manager the current player data.
    /// </summary>
    public PlayerDataSnapshot RequestPlayerData()
    {
        PlayerDataSnapshot data = BuildSnapshot();
        _ui?.PresentPlayerData(data);
        return data;
    }

    public PlayerDataSnapshot GetPlayerData()
    {
        return BuildSnapshot();
    }

    public bool HasProcessedTransaction(string transactionId)
    {
        return !string.IsNullOrEmpty(transactionId) && _appliedTransactionIds.Contains(transactionId);
    }

    public bool HasReportedCatch(string catchToken)
    {
        return !string.IsNullOrEmpty(catchToken) && _reportedCatchTokens.Contains(catchToken);
    }

    public bool IsEquipmentUnlocked(string equipmentId)
    {
        return IsUnlocked(equipmentId, UnlockKind.Equipment);
    }

    public bool IsBoatUpgradeUnlocked(string upgradeId)
    {
        return IsUnlocked(upgradeId, UnlockKind.BoatUpgrade);
    }

    public bool IsRegionUnlocked(string regionId)
    {
        return IsUnlocked(regionId, UnlockKind.Region);
    }

    /// <summary>
    /// Economy Manager entry. Runs PROCESS TRANSACTION, or queues it until IDLE.
    /// A Deferred result is confirmed later through TransactionResolved.
    /// </summary>
    public TransactionResult SubmitTransaction(TransactionRequest request)
    {
        if (ShouldQueue())
        {
            _pending.Enqueue(PendingWork.ForTransaction(request));
            return TransactionResult.Deferred(request.TransactionId);
        }

        TransactionResult result = RunTransaction(request);
        Drain();
        return result;
    }

    /// <summary>
    /// Fishing System entry. The catch token is reported to the World State Manager once.
    /// </summary>
    public CatchProcessResult SubmitCatch(CatchInfo catchInfo, string catchToken)
    {
        if (string.IsNullOrEmpty(catchToken))
            catchToken = Guid.NewGuid().ToString("N");

        if (ShouldQueue())
        {
            _pending.Enqueue(PendingWork.ForCatch(catchInfo, catchToken));
            return CatchProcessResult.Deferred(catchInfo, catchToken);
        }

        CatchProcessResult result = RunCatch(catchInfo, catchToken);
        Drain();
        return result;
    }

    /// <summary>
    /// IDLE transitions to PROGRESSION UPDATE when experience is earned outside a catch.
    /// </summary>
    public void ReportExperience(int amount)
    {
        if (amount <= 0)
            return;

        QueueOrRunProgression(amount);
    }

    /// <summary>
    /// IDLE transitions to PROGRESSION UPDATE when an unlock requirement is met.
    /// </summary>
    public void ReportUnlockRequirementMet(string requirementId)
    {
        if (string.IsNullOrWhiteSpace(requirementId))
            return;

        if (!_clearances.Add(requirementId))
            return;

        QueueOrRunProgression(0);
    }

    void HandleFishingCatch(CatchInfo catchInfo)
    {
        SubmitCatch(catchInfo, Guid.NewGuid().ToString("N"));
    }

    void QueueOrRunProgression(int experienceToAdd)
    {
        if (ShouldQueue())
        {
            _pending.Enqueue(PendingWork.ForProgression(experienceToAdd));
            return;
        }

        RunProgression(experienceToAdd);
        Drain();
    }

    bool ShouldQueue()
    {
        return _draining || CurrentState != PlayerManagerState.Idle;
    }

    void Drain()
    {
        if (_draining)
            return;

        _draining = true;
        try
        {
            while (_pending.Count > 0)
            {
                PendingWork work = _pending.Dequeue();
                switch (work.Kind)
                {
                    case PendingKind.Transaction:
                        RunTransaction(work.Transaction);
                        break;
                    case PendingKind.Catch:
                        RunCatch(work.Catch, work.Token);
                        break;
                    case PendingKind.Progression:
                        RunProgression(work.Experience);
                        break;
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    TransactionResult RunTransaction(TransactionRequest request)
    {
        SetState(PlayerManagerState.ProcessTransaction);
        TransactionResult result = default;
        try
        {
            result = ResolveTransaction(request);
            _ui?.OnTransactionResolved(result);
            TransactionResolved?.Invoke(result);
            onTransactionResolved?.Invoke();

            if (result.Status == TransactionStatus.Succeeded)
                NotifyDataChanged();
        }
        finally
        {
            if (CurrentState == PlayerManagerState.ProcessTransaction)
                SetState(PlayerManagerState.Idle);
        }

        return result;
    }

    CatchProcessResult RunCatch(CatchInfo catchInfo, string catchToken)
    {
        SetState(PlayerManagerState.ProcessCatch);
        CatchProcessResult result = default;
        try
        {
            result = ResolveCatch(catchInfo, catchToken);
            CatchProcessed?.Invoke(result);
            onCatchProcessed?.Invoke();

            if (result.Status == CatchProcessStatus.Accepted && NeedsProgression())
                RunProgression(0);
        }
        finally
        {
            if (CurrentState == PlayerManagerState.ProcessCatch)
                SetState(PlayerManagerState.Idle);
        }

        return result;
    }

    void RunProgression(int experienceToAdd)
    {
        SetState(PlayerManagerState.ProgressionUpdate);
        try
        {
            int experienceBefore = _experience;
            int previousRank = _rankIndex;
            if (experienceToAdd > 0)
                AddExperience(experienceToAdd);

            int qualifiedRank = HighestRankIndex(_experience);
            if (qualifiedRank > _rankIndex)
                _rankIndex = qualifiedRank;

            UnlockGrant[] grants = GrantQualifiedUnlocks(notify: true);
            bool rankIncreased = _rankIndex != previousRank;
            bool experienceChanged = _experience != experienceBefore;

            if (rankIncreased || grants.Length > 0)
            {
                ProgressionResult progression = new ProgressionResult
                {
                    ExperienceAdded = _experience - experienceBefore,
                    Experience = _experience,
                    RankIncreased = rankIncreased,
                    PreviousRankId = RankIdAt(previousRank),
                    PreviousRankName = RankNameAt(previousRank),
                    RankId = RankId,
                    RankName = RankName,
                    NewUnlocks = grants
                };

                _ui?.OnProgressionUpdated(progression);
                ProgressionUpdated?.Invoke(progression);
                onProgressionUpdated?.Invoke();
            }

            if (experienceChanged || rankIncreased || grants.Length > 0)
                NotifyDataChanged();
        }
        finally
        {
            if (CurrentState == PlayerManagerState.ProgressionUpdate)
                SetState(PlayerManagerState.Idle);
        }
    }

    TransactionResult ResolveTransaction(TransactionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
            return Rejected(request, "Transaction id is required.");

        if (_appliedTransactionIds.Contains(request.TransactionId))
            return MakeResult(request.TransactionId, TransactionStatus.Duplicate, "Transaction was already applied.");

        if (request.Quantity <= 0)
            return Rejected(request, "Quantity must be greater than zero.");

        if (request.UnitPrice < 0)
            return Rejected(request, "Price cannot be negative.");

        if (request.Kind == TransactionKind.Purchase)
            return ResolvePurchase(request);

        if (request.Kind == TransactionKind.Sale)
            return ResolveSale(request);

        return Rejected(request, "Unknown transaction kind.");
    }

    TransactionResult ResolvePurchase(TransactionRequest request)
    {
        if (!TryMultiply(request.Quantity, request.UnitPrice, out int cost))
            return Rejected(request, "Transaction total is invalid.");

        if (!allowDebt && _money < cost)
            return Rejected(request, "Not enough money.");

        bool grantUnlock = IsUnlockCategory(request.Category);
        UnlockDefinition unlockDefinition = grantUnlock ? FindUnlock(request.ItemId) : null;
        bool repeatable = unlockDefinition != null ? unlockDefinition.repeatable : request.Repeatable;
        UnlockKind unlockKind = unlockDefinition != null ? unlockDefinition.kind : ToUnlockKind(request.Category);
        if (grantUnlock && !repeatable && (request.Quantity != 1 || IsUnlocked(request.ItemId, unlockKind)))
            return Rejected(request, "Unlock is already owned.");

        bool usesSlots = request.Category == PlayerItemCategory.Fish || request.Category == PlayerItemCategory.CarriedItem;
        int slotCost = request.SlotCostPerUnit;
        if (usesSlots && slotCost <= 0)
            slotCost = 1;

        int slots = 0;
        if (usesSlots)
        {
            if (!TryMultiply(request.Quantity, slotCost, out slots) || (long)_slotsUsed + slots > inventoryCapacity)
                return Rejected(request, "Not enough inventory space.");
        }

        if (grantUnlock && string.IsNullOrWhiteSpace(request.ItemId))
            return Rejected(request, "Unlock id is required.");

        if ((request.Category == PlayerItemCategory.Fish || request.Category == PlayerItemCategory.CarriedItem) &&
            string.IsNullOrWhiteSpace(request.ItemId))
            return Rejected(request, "Item id is required.");

        long balance = (long)_money - cost;
        if (balance < int.MinValue)
            return Rejected(request, "Money total is too large.");

        string displayName = DisplayNameOf(request);
        if (unlockDefinition != null && string.IsNullOrEmpty(request.DisplayName) && !string.IsNullOrEmpty(unlockDefinition.displayName))
            displayName = unlockDefinition.displayName;

        SetMoney((int)balance);

        if (request.Category == PlayerItemCategory.Fish)
            AddStack(_fish, request.ItemId, displayName, CatchKind.Entity, request.Quantity, slotCost);
        else if (request.Category == PlayerItemCategory.CarriedItem)
            AddStack(_items, request.ItemId, displayName, CatchKind.Item, request.Quantity, slotCost);

        if (grantUnlock)
            GrantUnlock(request.ItemId, displayName, unlockKind, request.Quantity, repeatable, notify: true);

        RecalculateSlots();
        _appliedTransactionIds.Add(request.TransactionId);
        return MakeResult(request.TransactionId, TransactionStatus.Succeeded, "Transaction succeeded.");
    }

    TransactionResult ResolveSale(TransactionRequest request)
    {
        if (request.Category != PlayerItemCategory.Fish && request.Category != PlayerItemCategory.CarriedItem)
            return Rejected(request, "Only fish and carried items can be sold.");

        if (!TryMultiply(request.Quantity, request.UnitPrice, out int payout))
            return Rejected(request, "Transaction total is invalid.");

        if ((long)_money + payout > int.MaxValue)
            return Rejected(request, "Money total is too large.");

        List<InventoryStack> list = request.Category == PlayerItemCategory.Fish ? _fish : _items;
        if (!HasCount(list, request.ItemId, request.Quantity))
            return Rejected(request, "Not enough items to sell.");

        Remove(list, request.ItemId, request.Quantity);
        SetMoney(_money + payout);
        RecalculateSlots();
        _appliedTransactionIds.Add(request.TransactionId);
        return MakeResult(request.TransactionId, TransactionStatus.Succeeded, "Transaction succeeded.");
    }

    CatchProcessResult ResolveCatch(CatchInfo catchInfo, string catchToken)
    {
        if (_reportedCatchTokens.Contains(catchToken))
        {
            return new CatchProcessResult
            {
                Catch = catchInfo,
                Token = catchToken,
                Status = CatchProcessStatus.Duplicate,
                Reason = "Catch was already reported."
            };
        }

        if (string.IsNullOrWhiteSpace(catchInfo.Id))
        {
            return new CatchProcessResult
            {
                Catch = catchInfo,
                Token = catchToken,
                Status = CatchProcessStatus.Rejected,
                Reason = "Catch id is missing."
            };
        }

        int slotCost = 1;
        int experience = Mathf.Max(0, defaultCatchExperience);
        if (_rewardLookup.TryGetValue(catchInfo.Id, out CatchRewardDefinition reward))
        {
            slotCost = Mathf.Max(1, reward.slotCost);
            experience = Mathf.Max(0, reward.experience);
        }

        if ((long)_slotsUsed + slotCost > inventoryCapacity)
        {
            return new CatchProcessResult
            {
                Catch = catchInfo,
                Token = catchToken,
                Status = CatchProcessStatus.Rejected,
                Reason = "Not enough inventory space."
            };
        }

        List<InventoryStack> list = catchInfo.Kind == CatchKind.Entity ? _fish : _items;
        AddStack(list, catchInfo.Id, catchInfo.DisplayName, catchInfo.Kind, 1, slotCost);
        RecalculateSlots();
        AddExperience(experience);

        _reportedCatchTokens.Add(catchToken);
        WorldCatchNotice notice = new WorldCatchNotice
        {
            Token = catchToken,
            Catch = catchInfo
        };
        _world?.OnCatchKept(notice);
        NotifyDataChanged();

        return new CatchProcessResult
        {
            Catch = catchInfo,
            Token = catchToken,
            Status = CatchProcessStatus.Accepted,
            Reason = "Catch added.",
            ExperienceAwarded = experience
        };
    }

    bool NeedsProgression()
    {
        return HighestRankIndex(_experience) > _rankIndex || HasUngrantedQualifiedUnlock();
    }

    bool HasUngrantedQualifiedUnlock()
    {
        if (unlocks == null)
            return false;

        for (int i = 0; i < unlocks.Length; i++)
        {
            UnlockDefinition definition = unlocks[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.id) || !IsQualified(definition))
                continue;

            if (!_unlocks.TryGetValue(definition.id, out UnlockRecord owned) || owned.Count <= 0)
                return true;
        }

        return false;
    }

    UnlockGrant[] GrantQualifiedUnlocks(bool notify)
    {
        if (unlocks == null || unlocks.Length == 0)
            return Array.Empty<UnlockGrant>();

        List<UnlockGrant> grants = null;
        for (int i = 0; i < unlocks.Length; i++)
        {
            UnlockDefinition definition = unlocks[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.id) || !IsQualified(definition))
                continue;

            if (_unlocks.TryGetValue(definition.id, out UnlockRecord owned) && owned.Count > 0)
                continue;

            UnlockGrant grant = GrantUnlock(definition.id, definition.displayName, definition.kind, 1, definition.repeatable, notify);
            if (grants == null)
                grants = new List<UnlockGrant>();
            grants.Add(grant);
        }

        return grants == null ? Array.Empty<UnlockGrant>() : grants.ToArray();
    }

    bool IsQualified(UnlockDefinition definition)
    {
        if (_experience < definition.requiredExperience)
            return false;

        if (!string.IsNullOrEmpty(definition.requiredRankId))
        {
            int requiredRank = IndexOfRank(definition.requiredRankId);
            if (requiredRank < 0 || _rankIndex < requiredRank)
                return false;
        }

        if (!string.IsNullOrEmpty(definition.requiredClearanceId) && !_clearances.Contains(definition.requiredClearanceId))
            return false;

        return true;
    }

    UnlockGrant GrantUnlock(string id, string displayName, UnlockKind kind, int count, bool repeatable, bool notify)
    {
        if (!_unlocks.TryGetValue(id, out UnlockRecord record))
        {
            record = new UnlockRecord
            {
                Id = id,
                DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName,
                Kind = kind,
                Count = 0
            };
            _unlocks.Add(id, record);
        }

        record.Count += Mathf.Max(1, count);
        record.Kind = kind;
        if (!string.IsNullOrEmpty(displayName))
            record.DisplayName = displayName;

        UnlockGrant grant = new UnlockGrant
        {
            Id = record.Id,
            DisplayName = record.DisplayName,
            Kind = record.Kind,
            Count = record.Count,
            Repeatable = repeatable,
            IsInitialSync = false
        };

        if (notify)
            NotifyUnlock(grant);

        return grant;
    }

    void PublishOwnedUnlocks()
    {
        foreach (UnlockRecord record in _unlocks.Values)
        {
            if (record.Count <= 0)
                continue;

            NotifyUnlock(new UnlockGrant
            {
                Id = record.Id,
                DisplayName = record.DisplayName,
                Kind = record.Kind,
                Count = record.Count,
                IsInitialSync = true
            });
        }
    }

    void NotifyUnlock(UnlockGrant grant)
    {
        for (int i = 0; i < _receivers.Count; i++)
            _receivers[i].OnContentUnlocked(grant);
    }

    void NotifyDataChanged()
    {
        PlayerDataSnapshot data = BuildSnapshot();
        _ui?.OnPlayerDataChanged(data);
        PlayerDataChanged?.Invoke(data);
        onPlayerDataChanged?.Invoke();
    }

    PlayerDataSnapshot BuildSnapshot()
    {
        return new PlayerDataSnapshot
        {
            Money = _money,
            Experience = _experience,
            RankId = RankId,
            RankName = RankName,
            InventoryUsed = _slotsUsed,
            InventoryCapacity = inventoryCapacity,
            Fish = CopyStacks(_fish),
            CarriedItems = CopyStacks(_items),
            Equipment = IdsOfKind(UnlockKind.Equipment),
            BoatUpgrades = IdsOfKind(UnlockKind.BoatUpgrade),
            UnlockedRegions = IdsOfKind(UnlockKind.Region)
        };
    }

    void SetMoney(int value)
    {
        if (!allowDebt && value < 0)
            value = 0;

        _money = value;
    }

    void AddExperience(int amount)
    {
        if (amount <= 0)
            return;

        long sum = (long)_experience + amount;
        _experience = sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    void AddStack(List<InventoryStack> list, string id, string displayName, CatchKind kind, int count, int slotCost)
    {
        for (int i = 0; i < list.Count; i++)
        {
            InventoryStack stack = list[i];
            if (stack.Id == id && stack.DisplayName == displayName && stack.Kind == kind && stack.SlotCostPerUnit == slotCost)
            {
                stack.Count += count;
                return;
            }
        }

        list.Add(new InventoryStack
        {
            Id = id,
            DisplayName = displayName,
            Kind = kind,
            Count = count,
            SlotCostPerUnit = slotCost
        });
    }

    bool HasCount(List<InventoryStack> list, string id, int count)
    {
        return CountOf(list, id) >= count;
    }

    int CountOf(List<InventoryStack> list, string id)
    {
        int total = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == id)
                total += list[i].Count;
        }

        return total;
    }

    void Remove(List<InventoryStack> list, string id, int count)
    {
        int remaining = count;
        for (int i = 0; i < list.Count && remaining > 0; i++)
        {
            if (list[i].Id != id)
                continue;

            int take = Mathf.Min(remaining, list[i].Count);
            list[i].Count -= take;
            remaining -= take;
        }

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Count <= 0)
                list.RemoveAt(i);
        }
    }

    void RecalculateSlots()
    {
        _slotsUsed = CountSlots(_fish) + CountSlots(_items);
    }

    static int CountSlots(List<InventoryStack> list)
    {
        long total = 0;
        for (int i = 0; i < list.Count; i++)
        {
            total += (long)list[i].Count * list[i].SlotCostPerUnit;
            if (total >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)total;
    }

    void AddStartingInventory(StartingStack[] stacks, CatchKind kind, List<InventoryStack> destination)
    {
        if (stacks == null)
            return;

        for (int i = 0; i < stacks.Length; i++)
        {
            StartingStack stack = stacks[i];
            if (stack == null || string.IsNullOrWhiteSpace(stack.id) || stack.count <= 0)
                continue;

            int slotCost = Mathf.Max(1, stack.slotCostPerUnit);
            int used = CountSlots(_fish) + CountSlots(_items);
            if (!TryMultiply(stack.count, slotCost, out int slots) || used + slots > inventoryCapacity)
            {
                if (!_warnedSkippedStartingItems)
                {
                    _warnedSkippedStartingItems = true;
                    Debug.LogWarning("PlayerManager skipped starting inventory that does not fit the inventory capacity.", this);
                }

                continue;
            }

            AddStack(destination, stack.id, string.IsNullOrEmpty(stack.displayName) ? stack.id : stack.displayName, kind, stack.count, slotCost);
        }
    }

    void AddStartingUnlocks()
    {
        GrantStarting(startingEquipment, UnlockKind.Equipment);
        GrantStarting(startingBoatUpgrades, UnlockKind.BoatUpgrade);
        GrantStarting(startingRegions, UnlockKind.Region);
    }

    void GrantStarting(StartingUnlock[] entries, UnlockKind kind)
    {
        if (entries == null)
            return;

        foreach (StartingUnlock entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                continue;

            if (_unlocks.ContainsKey(entry.id))
                continue;

            _unlocks.Add(entry.id, new UnlockRecord
            {
                Id = entry.id,
                DisplayName = string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName,
                Kind = kind,
                Count = 1
            });
        }
    }

    void BuildRanks()
    {
        _ranks.Clear();
        bool hasBaseRank = false;
        if (ranks != null)
        {
            for (int i = 0; i < ranks.Length; i++)
            {
                RankDefinition rank = ranks[i];
                if (rank == null || string.IsNullOrWhiteSpace(rank.id))
                    continue;

                int required = Mathf.Max(0, rank.experienceRequired);
                if (required == 0)
                    hasBaseRank = true;

                _ranks.Add(new RankDefinition
                {
                    id = rank.id,
                    displayName = string.IsNullOrEmpty(rank.displayName) ? rank.id : rank.displayName,
                    experienceRequired = required
                });
            }
        }

        if (!hasBaseRank)
        {
            _ranks.Add(new RankDefinition
            {
                id = "novice",
                displayName = "Novice",
                experienceRequired = 0
            });
        }

        _ranks.Sort((a, b) => a.experienceRequired.CompareTo(b.experienceRequired));
        _rankIndex = HighestRankIndex(_experience);
    }

    void BuildRewardLookup()
    {
        _rewardLookup.Clear();
        if (catchRewards == null)
            return;

        for (int i = 0; i < catchRewards.Length; i++)
        {
            CatchRewardDefinition reward = catchRewards[i];
            if (reward == null || string.IsNullOrWhiteSpace(reward.catchId))
                continue;

            _rewardLookup[reward.catchId] = reward;
        }
    }

    int HighestRankIndex(int experience)
    {
        int index = 0;
        for (int i = 0; i < _ranks.Count; i++)
        {
            if (experience >= _ranks[i].experienceRequired)
                index = i;
            else
                break;
        }

        return index;
    }

    int IndexOfRank(string rankId)
    {
        for (int i = 0; i < _ranks.Count; i++)
        {
            if (_ranks[i].id == rankId)
                return i;
        }

        return -1;
    }

    string RankIdAt(int index)
    {
        if (index < 0 || index >= _ranks.Count)
            return string.Empty;

        return _ranks[index].id;
    }

    string RankNameAt(int index)
    {
        if (index < 0 || index >= _ranks.Count)
            return string.Empty;

        return _ranks[index].displayName;
    }

    UnlockDefinition FindUnlock(string id)
    {
        if (string.IsNullOrEmpty(id) || unlocks == null)
            return null;

        for (int i = 0; i < unlocks.Length; i++)
        {
            UnlockDefinition definition = unlocks[i];
            if (definition != null && definition.id == id)
                return definition;
        }

        return null;
    }

    bool IsUnlocked(string id, UnlockKind kind)
    {
        return !string.IsNullOrEmpty(id) &&
               _unlocks.TryGetValue(id, out UnlockRecord record) &&
               record.Kind == kind &&
               record.Count > 0;
    }

    static bool IsUnlockCategory(PlayerItemCategory category)
    {
        return category == PlayerItemCategory.Equipment ||
               category == PlayerItemCategory.BoatUpgrade ||
               category == PlayerItemCategory.Region;
    }

    static UnlockKind ToUnlockKind(PlayerItemCategory category)
    {
        switch (category)
        {
            case PlayerItemCategory.BoatUpgrade:
                return UnlockKind.BoatUpgrade;
            case PlayerItemCategory.Region:
                return UnlockKind.Region;
            default:
                return UnlockKind.Equipment;
        }
    }

    string[] IdsOfKind(UnlockKind kind)
    {
        List<string> ids = new List<string>();
        foreach (UnlockRecord record in _unlocks.Values)
        {
            if (record.Kind == kind && record.Count > 0)
                ids.Add(record.Id);
        }

        ids.Sort(StringComparer.Ordinal);
        return ids.ToArray();
    }

    static ItemRecord[] CopyStacks(List<InventoryStack> list)
    {
        ItemRecord[] records = new ItemRecord[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            InventoryStack stack = list[i];
            records[i] = new ItemRecord
            {
                Id = stack.Id,
                DisplayName = stack.DisplayName,
                Kind = stack.Kind,
                Count = stack.Count,
                SlotCostPerUnit = stack.SlotCostPerUnit
            };
        }

        return records;
    }

    void ResolveCollaborators()
    {
        _ui = uiManager as IPlayerUi;
        if (uiManager != null && _ui == null)
            Debug.LogWarning($"{nameof(PlayerManager)} on {name}: {uiManager.GetType().Name} does not implement {nameof(IPlayerUi)}.", this);

        _world = worldStateManager as IWorldStateManager;
        if (worldStateManager != null && _world == null)
            Debug.LogWarning($"{nameof(PlayerManager)} on {name}: {worldStateManager.GetType().Name} does not implement {nameof(IWorldStateManager)}.", this);

        _receivers.Clear();
        if (unlockReceivers == null)
            return;

        for (int i = 0; i < unlockReceivers.Length; i++)
        {
            MonoBehaviour behaviour = unlockReceivers[i];
            if (behaviour is IUnlockReceiver receiver)
                _receivers.Add(receiver);
            else if (behaviour != null)
                Debug.LogWarning($"{nameof(PlayerManager)} on {name}: {behaviour.GetType().Name} does not implement {nameof(IUnlockReceiver)}.", this);
        }
    }

    void SetState(PlayerManagerState state)
    {
        if (CurrentState == state)
            return;

        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    TransactionResult Rejected(TransactionRequest request, string reason)
    {
        return MakeResult(request.TransactionId, TransactionStatus.Rejected, reason);
    }

    TransactionResult MakeResult(string transactionId, TransactionStatus status, string reason)
    {
        return new TransactionResult
        {
            TransactionId = transactionId,
            Status = status,
            Reason = reason,
            Money = _money,
            InventoryUsed = _slotsUsed
        };
    }

    static string DisplayNameOf(TransactionRequest request)
    {
        return string.IsNullOrEmpty(request.DisplayName) ? request.ItemId : request.DisplayName;
    }

    static bool TryMultiply(int a, int b, out int product)
    {
        if (a < 0 || b < 0)
        {
            product = 0;
            return false;
        }

        long value = (long)a * b;
        if (value > int.MaxValue)
        {
            product = 0;
            return false;
        }

        product = (int)value;
        return true;
    }

    sealed class InventoryStack
    {
        public string Id;
        public string DisplayName;
        public CatchKind Kind;
        public int Count;
        public int SlotCostPerUnit;
    }

    sealed class UnlockRecord
    {
        public string Id;
        public string DisplayName;
        public UnlockKind Kind;
        public int Count;
    }

    enum PendingKind
    {
        Transaction,
        Catch,
        Progression
    }

    struct PendingWork
    {
        public PendingKind Kind;
        public TransactionRequest Transaction;
        public CatchInfo Catch;
        public string Token;
        public int Experience;

        public static PendingWork ForTransaction(TransactionRequest request)
        {
            return new PendingWork { Kind = PendingKind.Transaction, Transaction = request };
        }

        public static PendingWork ForCatch(CatchInfo catchInfo, string token)
        {
            return new PendingWork { Kind = PendingKind.Catch, Catch = catchInfo, Token = token };
        }

        public static PendingWork ForProgression(int experience)
        {
            return new PendingWork { Kind = PendingKind.Progression, Experience = experience };
        }
    }
}

public enum PlayerManagerState
{
    Idle,
    ProcessTransaction,
    ProcessCatch,
    ProgressionUpdate
}

public enum TransactionKind
{
    Purchase,
    Sale
}

public enum TransactionStatus
{
    Succeeded,
    Rejected,
    Duplicate,
    Deferred
}

public enum CatchProcessStatus
{
    Accepted,
    Rejected,
    Duplicate,
    Deferred
}

public enum PlayerItemCategory
{
    Fish,
    CarriedItem,
    Equipment,
    BoatUpgrade,
    Region
}

public enum UnlockKind
{
    Equipment,
    BoatUpgrade,
    Region
}

public struct TransactionRequest
{
    public string TransactionId;
    public TransactionKind Kind;
    public PlayerItemCategory Category;
    public string ItemId;
    public string DisplayName;
    public int Quantity;
    public int UnitPrice;
    public int SlotCostPerUnit;
    public bool Repeatable;
}

public struct TransactionResult
{
    public string TransactionId;
    public TransactionStatus Status;
    public string Reason;
    public int Money;
    public int InventoryUsed;

    public bool Succeeded => Status == TransactionStatus.Succeeded;

    public static TransactionResult Deferred(string transactionId)
    {
        return new TransactionResult
        {
            TransactionId = transactionId,
            Status = TransactionStatus.Deferred,
            Reason = "Transaction is waiting until the player manager is idle."
        };
    }
}

public struct CatchProcessResult
{
    public CatchInfo Catch;
    public string Token;
    public CatchProcessStatus Status;
    public string Reason;
    public int ExperienceAwarded;

    public bool Accepted => Status == CatchProcessStatus.Accepted;

    public static CatchProcessResult Deferred(CatchInfo catchInfo, string token)
    {
        return new CatchProcessResult
        {
            Catch = catchInfo,
            Token = token,
            Status = CatchProcessStatus.Deferred,
            Reason = "Catch is waiting until the player manager is idle."
        };
    }
}

public struct WorldCatchNotice
{
    public string Token;
    public CatchInfo Catch;
}

public struct ItemRecord
{
    public string Id;
    public string DisplayName;
    public CatchKind Kind;
    public int Count;
    public int SlotCostPerUnit;
}

public struct PlayerDataSnapshot
{
    public int Money;
    public int Experience;
    public string RankId;
    public string RankName;
    public int InventoryUsed;
    public int InventoryCapacity;
    public ItemRecord[] Fish;
    public ItemRecord[] CarriedItems;
    public string[] Equipment;
    public string[] BoatUpgrades;
    public string[] UnlockedRegions;
}

public struct UnlockGrant
{
    public string Id;
    public string DisplayName;
    public UnlockKind Kind;
    public int Count;
    public bool Repeatable;
    public bool IsInitialSync;
}

public struct ProgressionResult
{
    public int ExperienceAdded;
    public int Experience;
    public bool RankIncreased;
    public string PreviousRankId;
    public string PreviousRankName;
    public string RankId;
    public string RankName;
    public UnlockGrant[] NewUnlocks;
}

[Serializable]
public class RankDefinition
{
    public string id = "novice";
    public string displayName = "Novice";
    [Min(0)] public int experienceRequired;
}

[Serializable]
public class UnlockDefinition
{
    public string id;
    public string displayName;
    public UnlockKind kind = UnlockKind.Equipment;
    [Min(0)] public int requiredExperience;
    public string requiredRankId;
    public string requiredClearanceId;
    public bool repeatable;
}

[Serializable]
public class CatchRewardDefinition
{
    public string catchId;
    [Min(0)] public int experience = 10;
    [Min(1)] public int slotCost = 1;
}

[Serializable]
public class StartingStack
{
    public string id;
    public string displayName;
    [Min(1)] public int count = 1;
    [Min(1)] public int slotCostPerUnit = 1;
}

[Serializable]
public class StartingUnlock
{
    public string id;
    public string displayName;
}

public interface IPlayerUi
{
    void PresentPlayerData(PlayerDataSnapshot data);
    void OnPlayerDataChanged(PlayerDataSnapshot data);
    void OnTransactionResolved(TransactionResult result);
    void OnProgressionUpdated(ProgressionResult result);
}

public interface IWorldStateManager
{
    /// <summary>
    /// Called once for a catch token. The same token must not remove world population again.
    /// </summary>
    void OnCatchKept(WorldCatchNotice notice);
}

public interface IUnlockReceiver
{
    void OnContentUnlocked(UnlockGrant grant);
}
