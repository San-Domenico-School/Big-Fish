using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Market and transaction loop.
/// MARKET UPDATE prices each species from world population. That population is the market supply.
/// TRANSACTION prices one buy or sell, asks the Player Manager to apply it, then returns to MARKET UPDATE.
/// This component never changes the player's money or inventory itself.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Economy/Economy Manager")]
public class SufianS_EconomyManager : MonoBehaviour, IEconomyManager
{
    [Header("Fish Types")]
    [SerializeField] FishSpecies bluefinTuna = FishSpecies.Create("bluefin_tuna", "Bluefin Tuna", 90, 45, 180);
    [SerializeField] FishSpecies cod = FishSpecies.Create("cod", "Cod", 14, 7, 28);
    [SerializeField] FishSpecies kingCrab = FishSpecies.Create("king_crab", "King Crab", 40, 20, 80);
    [SerializeField] FishSpecies mackerel = FishSpecies.Create("mackerel", "Mackerel", 8, 4, 16);
    [SerializeField] FishSpecies salmon = FishSpecies.Create("salmon", "Salmon", 24, 12, 48);
    [SerializeField] FishSpecies seaBass = FishSpecies.Create("sea_bass", "Sea Bass", 18, 9, 36);
    [SerializeField] FishSpecies swordfish = FishSpecies.Create("swordfish", "Swordfish", 55, 28, 110);

    [Header("Equipment Prices")]
    [SerializeField] EquipmentPrice[] equipment = Array.Empty<EquipmentPrice>();

    [Header("Market Update")]
    [Min(0.01f)]
    [SerializeField] float marketUpdatePeriod = 20f;
    [Range(0f, 1f)]
    [SerializeField] float simulationStrength = 0.15f;
    [Min(0f)]
    [SerializeField] float simulationJitter = 1f;
    [Min(0f)]
    [SerializeField] float buyDemandGain = 0.5f;
    [Min(0f)]
    [SerializeField] float sellDemandLoss = 0.25f;

    [Header("References")]
    [Tooltip("Market supply for each species is the total population reported by this World State Manager.")]
    [SerializeField] WorldStateManager worldStateManager;
    [SerializeField] PlayerManager playerManager;
    [SerializeField] MonoBehaviour uiManager;

    readonly Dictionary<string, FishSpecies> _fishById = new Dictionary<string, FishSpecies>();
    readonly Dictionary<string, EquipmentPrice> _equipmentById = new Dictionary<string, EquipmentPrice>();
    readonly Dictionary<string, float> _restingDemand = new Dictionary<string, float>();

    IEconomyUi _ui;
    PricedTrade _activeTrade;
    bool _waitingForPlayer;
    bool _supplyDirty;
    float _marketTimer;
    bool _warnedMissingPlayer;
    bool _warnedMissingBasePrice;

    public EconomyState CurrentState { get; private set; } = EconomyState.MarketUpdate;
    public bool IsMarketUpdating => CurrentState == EconomyState.MarketUpdate && !_waitingForPlayer;

    public event Action<EconomyState> StateChanged;
    public event Action<MarketReport> MarketUpdated;
    public event Action<EconomyTradeResult> TradeFinished;

    void Awake()
    {
        EnsureFish();
        RebuildLookups();
        RefreshPrices();
        ResolveUi();
    }

    void OnEnable()
    {
        if (worldStateManager != null)
            worldStateManager.PopulationChanged += HandlePopulationChanged;
    }

    void Start()
    {
        RefreshPrices();
        PublishMarket();
    }

    void OnDisable()
    {
        if (worldStateManager != null)
            worldStateManager.PopulationChanged -= HandlePopulationChanged;
    }

    void OnValidate()
    {
        EnsureFish();
        RefreshPrices();
    }

    void Update()
    {
        if (CurrentState != EconomyState.MarketUpdate || _waitingForPlayer)
            return;

        _marketTimer += Time.deltaTime;
        if (_marketTimer < marketUpdatePeriod)
            return;

        _marketTimer = 0f;
        SimulateMarketActivity();
        RefreshPrices();
        PublishMarket();
    }

    /// <summary>
    /// UI entry. Leaves MARKET UPDATE for one buy or sell, then returns when that trade finishes or is rejected.
    /// </summary>
    public EconomyTradeResult RequestTrade(TradeOrder order)
    {
        if (CurrentState != EconomyState.MarketUpdate || _waitingForPlayer)
        {
            return Rejected(order, "A transaction is already in progress.");
        }

        SetState(EconomyState.Transaction);

        if (!TryPrice(order, out PricedTrade trade, out string priceError))
            return Fail(Rejected(order, priceError));

        if (playerManager == null)
        {
            WarnMissingPlayer();
            return Fail(Rejected(trade, "Player Manager is not assigned."));
        }

        TransactionRequest request = BuildRequest(trade);
        ResourceCheck resources = playerManager.HasSufficientResources(request);
        if (!resources.IsSufficient)
        {
            EconomyTradeResult rejected = FromCheck(trade, resources);
            Conclude(trade, rejected.Result, applyMarket: false);
            return rejected;
        }

        TransactionResult result = playerManager.CompleteTransaction(request);
        if (result.Status == TransactionStatus.Deferred)
        {
            _activeTrade = trade;
            _waitingForPlayer = true;
            return ToTradeResult(trade, result);
        }

        bool affectsMarket = result.Status == TransactionStatus.Succeeded && trade.AffectsFishMarket;
        Conclude(trade, result, affectsMarket);
        return ToTradeResult(trade, result);
    }

    public void OnPlayerTransactionFinished(TransactionResult result)
    {
        if (!_waitingForPlayer || result.TransactionId != _activeTrade.TransactionId)
            return;

        PricedTrade trade = _activeTrade;
        _waitingForPlayer = false;
        bool affectsMarket = result.Status == TransactionStatus.Succeeded && trade.AffectsFishMarket;
        Conclude(trade, result, affectsMarket);
    }

    public MarketReport GetMarketReport()
    {
        FishSpecies[] species = AllFish();
        SpeciesQuote[] quotes = new SpeciesQuote[species.Length];
        for (int i = 0; i < species.Length; i++)
            quotes[i] = ToQuote(species[i]);

        return new MarketReport { Species = quotes };
    }

    public bool TryGetFishPrice(string speciesId, out int price)
    {
        if (!string.IsNullOrEmpty(speciesId) && _fishById.TryGetValue(speciesId, out FishSpecies species))
        {
            price = species.currentPrice;
            return true;
        }

        price = 0;
        return false;
    }

    void Conclude(PricedTrade trade, TransactionResult result, bool applyMarket)
    {
        if (applyMarket)
        {
            ApplyFishMarketEffect(trade.ItemId, trade.Kind, trade.Quantity);
            RefreshPrices();
            PublishMarket();
        }

        ReturnToMarket(ToTradeResult(trade, result));
    }

    EconomyTradeResult Fail(EconomyTradeResult result)
    {
        ReturnToMarket(result);
        return result;
    }

    void ReturnToMarket(EconomyTradeResult result)
    {
        SetState(EconomyState.MarketUpdate);
        if (_supplyDirty)
        {
            RefreshPrices();
            PublishMarket();
        }

        _ui?.OnTradeFinished(result);
        TradeFinished?.Invoke(result);
    }

    bool TryPrice(TradeOrder order, out PricedTrade trade, out string error)
    {
        trade = default;
        error = null;

        if (order.Quantity <= 0)
        {
            error = "Quantity must be greater than zero.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(order.ItemId))
        {
            error = "Item id is required.";
            return false;
        }

        string transactionId = string.IsNullOrWhiteSpace(order.TransactionId)
            ? Guid.NewGuid().ToString("N")
            : order.TransactionId;

        if (order.Category == PlayerItemCategory.Fish)
        {
            if (!_fishById.TryGetValue(order.ItemId, out FishSpecies species))
            {
                error = "Species is not on the market.";
                return false;
            }

            if (species.basePrice <= 0)
            {
                WarnMissingBasePrice(species);
                error = "Species base price is not defined.";
                return false;
            }

            int unitPrice = species.currentPrice;
            if (!TryMultiply(order.Quantity, unitPrice, out int total))
            {
                error = "Transaction total is invalid.";
                return false;
            }

            trade = new PricedTrade
            {
                TransactionId = transactionId,
                Kind = order.Kind,
                Category = PlayerItemCategory.Fish,
                ItemId = species.id,
                DisplayName = species.displayName,
                Quantity = order.Quantity,
                UnitPrice = unitPrice,
                TotalValue = total,
                SlotCostPerUnit = Mathf.Max(1, species.slotCost),
                AffectsFishMarket = true
            };
            return true;
        }

        if (order.Category == PlayerItemCategory.Equipment)
        {
            if (!_equipmentById.TryGetValue(order.ItemId, out EquipmentPrice listing) || listing.price < 0)
            {
                error = "Equipment is not priced.";
                return false;
            }

            if (!TryMultiply(order.Quantity, listing.price, out int total))
            {
                error = "Transaction total is invalid.";
                return false;
            }

            trade = new PricedTrade
            {
                TransactionId = transactionId,
                Kind = order.Kind,
                Category = PlayerItemCategory.Equipment,
                ItemId = listing.id,
                DisplayName = string.IsNullOrEmpty(listing.displayName) ? listing.id : listing.displayName,
                Quantity = order.Quantity,
                UnitPrice = listing.price,
                TotalValue = total,
                Repeatable = listing.repeatable,
                AffectsFishMarket = false
            };
            return true;
        }

        error = "Only fish and equipment can be traded on this market.";
        return false;
    }

    static TransactionRequest BuildRequest(PricedTrade trade)
    {
        return new TransactionRequest
        {
            TransactionId = trade.TransactionId,
            Kind = trade.Kind,
            Category = trade.Category,
            ItemId = trade.ItemId,
            DisplayName = trade.DisplayName,
            Quantity = trade.Quantity,
            TotalValue = trade.TotalValue,
            SlotCostPerUnit = trade.SlotCostPerUnit,
            Repeatable = trade.Repeatable
        };
    }

    void ApplyFishMarketEffect(string speciesId, TransactionKind kind, int quantity)
    {
        if (!_fishById.TryGetValue(speciesId, out FishSpecies species))
            return;

        if (kind == TransactionKind.Sale)
        {
            species.demand = Mathf.Max(0f, species.demand - sellDemandLoss * quantity);
            return;
        }

        species.demand += buyDemandGain * quantity;
    }

    void SimulateMarketActivity()
    {
        FishSpecies[] species = AllFish();
        for (int i = 0; i < species.Length; i++)
        {
            FishSpecies fish = species[i];
            if (fish == null || string.IsNullOrEmpty(fish.id))
                continue;

            float restingDemand = _restingDemand.TryGetValue(fish.id, out float demand) ? demand : fish.demand;
            float jitter = UnityEngine.Random.Range(-simulationJitter, simulationJitter);
            fish.demand = Mathf.Max(0f, Mathf.Lerp(fish.demand, restingDemand, simulationStrength) + jitter);
        }
    }

    void RefreshPrices()
    {
        FishSpecies[] species = AllFish();
        for (int i = 0; i < species.Length; i++)
        {
            FishSpecies fish = species[i];
            if (fish == null)
                continue;

            Sanitize(fish);
            PullSupply(fish);
            fish.currentPrice = CalculatePrice(fish);
        }
    }

    int CalculatePrice(FishSpecies fish)
    {
        float supply = Mathf.Max(fish.supply, 0.01f);
        float raw = fish.basePrice * (fish.demand / supply);
        int price = Mathf.RoundToInt(raw);
        return Mathf.Clamp(price, fish.minPrice, fish.maxPrice);
    }

    void PublishMarket()
    {
        MarketReport report = GetMarketReport();
        _ui?.PresentMarket(report);
        MarketUpdated?.Invoke(report);
    }

    void EnsureFish()
    {
        bluefinTuna = FishSpecies.Ensure(bluefinTuna, "bluefin_tuna", "Bluefin Tuna", 90, 45, 180);
        cod = FishSpecies.Ensure(cod, "cod", "Cod", 14, 7, 28);
        kingCrab = FishSpecies.Ensure(kingCrab, "king_crab", "King Crab", 40, 20, 80);
        mackerel = FishSpecies.Ensure(mackerel, "mackerel", "Mackerel", 8, 4, 16);
        salmon = FishSpecies.Ensure(salmon, "salmon", "Salmon", 24, 12, 48);
        seaBass = FishSpecies.Ensure(seaBass, "sea_bass", "Sea Bass", 18, 9, 36);
        swordfish = FishSpecies.Ensure(swordfish, "swordfish", "Swordfish", 55, 28, 110);
    }

    void RebuildLookups()
    {
        _fishById.Clear();
        _equipmentById.Clear();
        _restingDemand.Clear();

        FishSpecies[] species = AllFish();
        for (int i = 0; i < species.Length; i++)
        {
            FishSpecies fish = species[i];
            if (fish == null || string.IsNullOrWhiteSpace(fish.id))
                continue;

            if (_fishById.ContainsKey(fish.id))
            {
                Debug.LogWarning($"Economy Manager already has a fish species named {fish.id}.", this);
                continue;
            }

            _fishById.Add(fish.id, fish);
            _restingDemand.Add(fish.id, Mathf.Max(0f, fish.demand));
        }

        if (equipment == null)
            return;

        for (int i = 0; i < equipment.Length; i++)
        {
            EquipmentPrice listing = equipment[i];
            if (listing == null || string.IsNullOrWhiteSpace(listing.id))
                continue;

            _equipmentById[listing.id] = listing;
        }
    }

    FishSpecies[] AllFish()
    {
        return new[]
        {
            bluefinTuna,
            cod,
            kingCrab,
            mackerel,
            salmon,
            seaBass,
            swordfish
        };
    }

    void PullSupply(FishSpecies fish)
    {
        if (!Application.isPlaying || worldStateManager == null || fish == null || string.IsNullOrEmpty(fish.id))
            return;

        fish.supply = worldStateManager.GetSpeciesPopulation(fish.id);
        _supplyDirty = false;
    }

    void HandlePopulationChanged()
    {
        if (CurrentState != EconomyState.MarketUpdate || _waitingForPlayer)
        {
            _supplyDirty = true;
            return;
        }

        RefreshPrices();
        PublishMarket();
    }

    void ResolveUi()
    {
        _ui = uiManager as IEconomyUi;
        if (uiManager != null && _ui == null)
            Debug.LogWarning($"{nameof(SufianS_EconomyManager)} on {name}: {uiManager.GetType().Name} does not implement {nameof(IEconomyUi)}.", this);
    }

    void SetState(EconomyState state)
    {
        if (CurrentState == state)
            return;

        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    void WarnMissingPlayer()
    {
        if (_warnedMissingPlayer)
            return;

        _warnedMissingPlayer = true;
        Debug.LogWarning("Economy Manager has no Player Manager assigned.", this);
    }

    void WarnMissingBasePrice(FishSpecies species)
    {
        if (_warnedMissingBasePrice)
            return;

        _warnedMissingBasePrice = true;
        Debug.LogWarning($"Fish species {species.id} has no base price.", this);
    }

    static void Sanitize(FishSpecies fish)
    {
        fish.basePrice = Mathf.Max(1, fish.basePrice);
        fish.minPrice = Mathf.Max(1, fish.minPrice);
        fish.maxPrice = Mathf.Max(fish.minPrice, fish.maxPrice);
        fish.basePrice = Mathf.Clamp(fish.basePrice, fish.minPrice, fish.maxPrice);
        fish.supply = Mathf.Max(0f, fish.supply);
        fish.demand = Mathf.Max(0f, fish.demand);
        fish.slotCost = Mathf.Max(1, fish.slotCost);
    }

    static SpeciesQuote ToQuote(FishSpecies fish)
    {
        return new SpeciesQuote
        {
            Id = fish.id,
            DisplayName = fish.displayName,
            BasePrice = fish.basePrice,
            MinPrice = fish.minPrice,
            MaxPrice = fish.maxPrice,
            Supply = fish.supply,
            Demand = fish.demand,
            Price = fish.currentPrice
        };
    }

    static EconomyTradeResult ToTradeResult(PricedTrade trade, TransactionResult result)
    {
        return new EconomyTradeResult
        {
            TransactionId = trade.TransactionId,
            ItemId = trade.ItemId,
            UnitPrice = trade.UnitPrice,
            TotalValue = trade.TotalValue,
            Result = result
        };
    }

    static EconomyTradeResult FromCheck(PricedTrade trade, ResourceCheck resources)
    {
        TransactionStatus status = resources.Status == TransactionStatus.Succeeded
            ? TransactionStatus.Rejected
            : resources.Status;

        return ToTradeResult(trade, new TransactionResult
        {
            TransactionId = trade.TransactionId,
            Status = status,
            Reason = resources.Reason,
            Money = resources.Money,
            InventoryUsed = resources.InventoryUsed
        });
    }

    static EconomyTradeResult Rejected(TradeOrder order, string reason)
    {
        return Rejected(order.TransactionId, order.ItemId, reason);
    }

    static EconomyTradeResult Rejected(PricedTrade trade, string reason)
    {
        return Rejected(trade.TransactionId, trade.ItemId, reason);
    }

    static EconomyTradeResult Rejected(string transactionId, string itemId, string reason)
    {
        return new EconomyTradeResult
        {
            TransactionId = transactionId,
            ItemId = itemId,
            Result = new TransactionResult
            {
                TransactionId = transactionId,
                Status = TransactionStatus.Rejected,
                Reason = reason
            }
        };
    }

    static bool TryMultiply(int quantity, int unitPrice, out int total)
    {
        if (quantity <= 0 || unitPrice < 0)
        {
            total = 0;
            return false;
        }

        long value = (long)quantity * unitPrice;
        if (value > int.MaxValue)
        {
            total = 0;
            return false;
        }

        total = (int)value;
        return true;
    }

    struct PricedTrade
    {
        public string TransactionId;
        public TransactionKind Kind;
        public PlayerItemCategory Category;
        public string ItemId;
        public string DisplayName;
        public int Quantity;
        public int UnitPrice;
        public int TotalValue;
        public int SlotCostPerUnit;
        public bool Repeatable;
        public bool AffectsFishMarket;
    }
}

public enum EconomyState
{
    MarketUpdate,
    Transaction
}

public struct TradeOrder
{
    public string TransactionId;
    public TransactionKind Kind;
    public PlayerItemCategory Category;
    public string ItemId;
    public int Quantity;
}

public struct SpeciesQuote
{
    public string Id;
    public string DisplayName;
    public int BasePrice;
    public int MinPrice;
    public int MaxPrice;
    public float Supply;
    public float Demand;
    public int Price;
}

public struct MarketReport
{
    public SpeciesQuote[] Species;
}

public struct EconomyTradeResult
{
    public string TransactionId;
    public string ItemId;
    public int UnitPrice;
    public int TotalValue;
    public TransactionResult Result;
}

[Serializable]
public class FishSpecies
{
    public string id;
    public string displayName;
    [Min(1)] public int basePrice = 10;
    [Min(1)] public int minPrice = 1;
    [Min(1)] public int maxPrice = 100;
    [Min(0f)] public float supply = 10f;
    [Min(0f)] public float demand = 10f;
    [Min(1)] public int slotCost = 1;
    [NonSerialized] public int currentPrice;

    public static FishSpecies Create(string id, string displayName, int basePrice, int minPrice, int maxPrice)
    {
        return new FishSpecies
        {
            id = id,
            displayName = displayName,
            basePrice = basePrice,
            minPrice = minPrice,
            maxPrice = maxPrice,
            supply = 10f,
            demand = 10f,
            slotCost = 1
        };
    }

    public static FishSpecies Ensure(FishSpecies species, string id, string displayName, int basePrice, int minPrice, int maxPrice)
    {
        if (species == null)
            species = Create(id, displayName, basePrice, minPrice, maxPrice);

        if (string.IsNullOrWhiteSpace(species.id))
            species.id = id;

        if (string.IsNullOrWhiteSpace(species.displayName))
            species.displayName = displayName;

        if (species.basePrice <= 0)
            species.basePrice = basePrice;

        if (species.minPrice <= 0)
            species.minPrice = minPrice;

        if (species.maxPrice <= 0)
            species.maxPrice = maxPrice;

        return species;
    }
}

[Serializable]
public class EquipmentPrice
{
    public string id;
    public string displayName;
    [Min(0)] public int price;
    public bool repeatable;
}

public interface IEconomyUi
{
    void PresentMarket(MarketReport report);
    void OnTradeFinished(EconomyTradeResult result);
}
