using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World population loop.
/// ACTIVE tracks regional fish populations, habitat, and catches.
/// REGENERATION reproduces each species from the habitat, then returns to ACTIVE.
/// Populations stay at or above zero and at or below the habitat-limited regional maximum.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("World/World State Manager")]
public class WorldStateManager : MonoBehaviour, IWorldStateManager
{
    [Header("Regions")]
    [SerializeField] FishingRegion[] regions = Array.Empty<FishingRegion>();
    [SerializeField] string activeRegionId = "coast";

    [Header("Species Ecology")]
    [SerializeField] SpeciesEcology bluefinTuna = SpeciesEcology.Create("bluefin_tuna", 0.08f, coral: true);
    [SerializeField] SpeciesEcology cod = SpeciesEcology.Create("cod", 0.16f, kelp: true);
    [SerializeField] SpeciesEcology kingCrab = SpeciesEcology.Create("king_crab", 0.1f, seagrass: true);
    [SerializeField] SpeciesEcology mackerel = SpeciesEcology.Create("mackerel", 0.22f, kelp: true, seagrass: true);
    [SerializeField] SpeciesEcology salmon = SpeciesEcology.Create("salmon", 0.14f, kelp: true, seagrass: true);
    [SerializeField] SpeciesEcology seaBass = SpeciesEcology.Create("sea_bass", 0.15f, kelp: true, seagrass: true);
    [SerializeField] SpeciesEcology swordfish = SpeciesEcology.Create("swordfish", 0.07f, coral: true);

    [Header("Regeneration")]
    [Tooltip("Length of the in-game day. ACTIVE changes to REGENERATION when this period ends.")]
    [Min(1f)]
    [SerializeField] float regenerationPeriod = 120f;
    [Range(0f, 1f)]
    [SerializeField] float scarceFraction = 0.2f;
    [Range(0f, 1f)]
    [Tooltip("Lowest share of the regional maximum that ruined habitat can still support.")]
    [SerializeField] float minimumCapacityFactor = 0.15f;

    [Header("References")]
    [SerializeField] MonoBehaviour uiManager;
    [SerializeField] MonoBehaviour environmentView;

    readonly HashSet<string> _caughtTokens = new HashSet<string>();
    readonly Dictionary<string, SpeciesEcology> _ecologyById = new Dictionary<string, SpeciesEcology>();

    IWorldUi _ui;
    IWorldEnvironment _environment;
    float _dayTimer;
    bool _warnedMissingRegion;

    public WorldActivityState CurrentState { get; private set; } = WorldActivityState.Active;
    public string ActiveRegionId => activeRegionId;

    public event Action<WorldActivityState> StateChanged;
    public event Action<string> PopulationChanged;

    void Awake()
    {
        EnsureContent();
        RebuildEcology();
        ResolveViews();
    }

    void Start()
    {
        Publish(null);
    }

    void OnValidate()
    {
        EnsureContent();
    }

    void Update()
    {
        if (CurrentState != WorldActivityState.Active)
            return;

        _dayTimer += Time.deltaTime;
        if (_dayTimer < regenerationPeriod)
            return;

        _dayTimer = 0f;
        _caughtTokens.Clear();
        RunRegeneration();
    }

    public void OnCatchKept(WorldCatchNotice notice)
    {
        if (!string.IsNullOrEmpty(notice.Token) && !_caughtTokens.Add(notice.Token))
            return;

        if (notice.Catch.Kind != CatchKind.Entity || string.IsNullOrWhiteSpace(notice.Catch.Id))
            return;

        if (!CanCatch(activeRegionId, notice.Catch.Id))
            return;

        SpeciesPopulation stock = FindStock(activeRegionId, notice.Catch.Id);
        stock.population = Mathf.Max(0, stock.population - 1);
        stock.status = EvaluateStatus(stock);
        Publish(null);
        NotifyPopulation(notice.Catch.Id);
    }

    public void SetActiveRegion(string regionId)
    {
        if (string.IsNullOrWhiteSpace(regionId) || FindRegion(regionId) == null)
            return;

        activeRegionId = regionId;
    }

    public bool CanCatch(string regionId, string speciesId)
    {
        SpeciesPopulation stock = FindStock(regionId, speciesId);
        return stock != null && stock.population > 0;
    }

    public int GetPopulation(string regionId, string speciesId)
    {
        SpeciesPopulation stock = FindStock(regionId, speciesId);
        return stock == null ? 0 : Mathf.Max(0, stock.population);
    }

    /// <summary>
    /// Total living population of one species across every region. This is the economy's market supply.
    /// </summary>
    public int GetSpeciesPopulation(string speciesId)
    {
        if (string.IsNullOrWhiteSpace(speciesId) || regions == null)
            return 0;

        int total = 0;
        for (int i = 0; i < regions.Length; i++)
        {
            SpeciesPopulation stock = FindStock(regions[i], speciesId);
            if (stock != null)
                total += Mathf.Max(0, stock.population);
        }

        return total;
    }

    public int GetEffectiveCapacity(string regionId, string speciesId)
    {
        FishingRegion region = FindRegion(regionId);
        SpeciesPopulation stock = FindStock(region, speciesId);
        SpeciesEcology ecology = FindEcology(speciesId);
        if (region == null || stock == null || ecology == null)
            return 0;

        return EffectiveCapacity(stock, region.habitat, ecology);
    }

    public bool RestoreHabitat(string regionId, HabitatFactor factor, float amount)
    {
        FishingRegion region = FindRegion(regionId);
        if (region == null || amount == 0f)
            return false;

        switch (factor)
        {
            case HabitatFactor.CoralReef:
                region.habitat.coralReef = Mathf.Clamp01(region.habitat.coralReef + amount);
                break;
            case HabitatFactor.Kelp:
                region.habitat.kelp = Mathf.Clamp01(region.habitat.kelp + amount);
                break;
            case HabitatFactor.Seagrass:
                region.habitat.seagrass = Mathf.Clamp01(region.habitat.seagrass + amount);
                break;
            case HabitatFactor.Pollution:
                region.habitat.pollution = Mathf.Clamp01(region.habitat.pollution - amount);
                break;
        }

        SpeciesPopulation[] stocks = region.AllStocks();
        int[] populationBefore = new int[stocks.Length];
        for (int i = 0; i < stocks.Length; i++)
            populationBefore[i] = stocks[i] == null ? 0 : stocks[i].population;

        ClampRegionToCapacity(region);
        Publish(null);
        for (int i = 0; i < stocks.Length; i++)
        {
            if (stocks[i] != null && stocks[i].population != populationBefore[i])
                NotifyPopulation(stocks[i].speciesId);
        }

        return true;
    }

    public bool Reintroduce(string regionId, string speciesId, int amount)
    {
        SpeciesEcology ecology = FindEcology(speciesId);
        SpeciesPopulation stock = FindStock(regionId, speciesId);
        FishingRegion region = FindRegion(regionId);
        if (ecology == null || !ecology.canBeReintroduced || stock == null || region == null || stock.population > 0 || amount <= 0)
            return false;

        int capacity = EffectiveCapacity(stock, region.habitat, ecology);
        stock.population = Mathf.Clamp(amount, 0, capacity);
        stock.status = EvaluateStatus(stock);
        Publish(null);
        NotifyPopulation(speciesId);
        return stock.population > 0;
    }

    public WorldSnapshot GetSnapshot()
    {
        EnsureContent();
        List<RegionalPopulation> populations = new List<RegionalPopulation>();
        if (regions != null)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                FishingRegion region = regions[i];
                if (region == null)
                    continue;

                SpeciesPopulation[] stocks = region.AllStocks();
                for (int s = 0; s < stocks.Length; s++)
                {
                    SpeciesPopulation stock = stocks[s];
                    if (stock == null || string.IsNullOrEmpty(stock.speciesId))
                        continue;

                    SpeciesEcology ecology = FindEcology(stock.speciesId);
                    int capacity = ecology == null ? stock.maxPopulation : EffectiveCapacity(stock, region.habitat, ecology);
                    populations.Add(new RegionalPopulation
                    {
                        RegionId = region.id,
                        RegionName = region.displayName,
                        SpeciesId = stock.speciesId,
                        Population = Mathf.Max(0, stock.population),
                        MaxPopulation = Mathf.Max(0, stock.maxPopulation),
                        Capacity = capacity,
                        Status = stock.population <= 0 ? PopulationStatus.Extinct : EvaluateStatus(stock),
                        Available = stock.population > 0
                    });
                }
            }
        }

        return new WorldSnapshot
        {
            ActiveRegionId = activeRegionId,
            Populations = populations.ToArray()
        };
    }

    void RunRegeneration()
    {
        SetState(WorldActivityState.Regeneration);
        List<SpeciesChange> changes = new List<SpeciesChange>();
        HashSet<string> changedSpecies = new HashSet<string>();

        if (regions != null)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                FishingRegion region = regions[i];
                if (region == null)
                    continue;

                SpeciesPopulation[] stocks = region.AllStocks();
                for (int s = 0; s < stocks.Length; s++)
                {
                    SpeciesPopulation stock = stocks[s];
                    int before = stock == null ? 0 : stock.population;
                    RegenerateStock(region, stock, changes);
                    if (stock != null && stock.population != before && !string.IsNullOrEmpty(stock.speciesId))
                        changedSpecies.Add(stock.speciesId);
                }
            }
        }

        RegenerationReport report = new RegenerationReport { Changes = changes.ToArray() };
        SetState(WorldActivityState.Active);
        Publish(report);
        foreach (string speciesId in changedSpecies)
            NotifyPopulation(speciesId);
    }

    void RegenerateStock(FishingRegion region, SpeciesPopulation stock, List<SpeciesChange> changes)
    {
        if (stock == null || string.IsNullOrEmpty(stock.speciesId))
            return;

        SpeciesEcology ecology = FindEcology(stock.speciesId);
        if (ecology == null)
            return;

        PopulationStatus previous = stock.population <= 0 ? PopulationStatus.Extinct : EvaluateStatus(stock);
        int capacity = EffectiveCapacity(stock, region.habitat, ecology);
        stock.maxPopulation = Mathf.Max(0, stock.maxPopulation);
        capacity = Mathf.Clamp(capacity, 0, stock.maxPopulation);

        if (stock.population <= 0)
        {
            stock.population = 0;
            stock.status = PopulationStatus.Extinct;
            RecordChange(changes, region.id, stock, previous, 0);
            return;
        }

        float habitat = HabitatModifier(ecology, region.habitat);
        float room = capacity <= 0 ? 0f : Mathf.Clamp01(1f - (stock.population / (float)capacity));
        int growth = Mathf.Max(0, Mathf.RoundToInt(stock.population * ecology.baseReproductionRate * habitat * room));
        stock.population = Mathf.Clamp(stock.population + growth, 0, capacity);
        stock.status = EvaluateStatus(stock);
        RecordChange(changes, region.id, stock, previous, growth);
    }

    void RecordChange(List<SpeciesChange> changes, string regionId, SpeciesPopulation stock, PopulationStatus previous, int growth)
    {
        PopulationStatus current = stock.status;
        bool recovered = previous != PopulationStatus.Healthy && current == PopulationStatus.Healthy;
        bool scarce = previous != PopulationStatus.Scarce && current == PopulationStatus.Scarce;
        bool extinct = previous != PopulationStatus.Extinct && current == PopulationStatus.Extinct;
        if (growth == 0 && !recovered && !scarce && !extinct)
            return;

        if (recovered)
            current = PopulationStatus.Recovered;

        stock.status = current == PopulationStatus.Recovered ? PopulationStatus.Healthy : current;
        changes.Add(new SpeciesChange
        {
            RegionId = regionId,
            SpeciesId = stock.speciesId,
            Previous = previous,
            Current = current,
            Population = stock.population,
            Growth = growth,
            Recovered = recovered,
            BecameScarce = scarce,
            WentExtinct = extinct
        });
    }

    void ClampRegionToCapacity(FishingRegion region)
    {
        SpeciesPopulation[] stocks = region.AllStocks();
        for (int i = 0; i < stocks.Length; i++)
        {
            SpeciesPopulation stock = stocks[i];
            SpeciesEcology ecology = stock == null ? null : FindEcology(stock.speciesId);
            if (stock == null || ecology == null)
                continue;

            int capacity = EffectiveCapacity(stock, region.habitat, ecology);
            stock.population = Mathf.Clamp(stock.population, 0, capacity);
            stock.status = EvaluateStatus(stock);
        }
    }

    int EffectiveCapacity(SpeciesPopulation stock, RegionHabitat habitat, SpeciesEcology ecology)
    {
        int maximum = Mathf.Max(0, stock.maxPopulation);
        float habitatFactor = Mathf.Lerp(minimumCapacityFactor, 1f, HabitatModifier(ecology, habitat));
        return Mathf.Clamp(Mathf.RoundToInt(maximum * habitatFactor), 0, maximum);
    }

    static float HabitatModifier(SpeciesEcology ecology, RegionHabitat habitat)
    {
        float support = 0f;
        int channels = 0;
        if (ecology.respondsToCoral)
        {
            support += habitat.coralReef;
            channels++;
        }

        if (ecology.respondsToKelp)
        {
            support += habitat.kelp;
            channels++;
        }

        if (ecology.respondsToSeagrass)
        {
            support += habitat.seagrass;
            channels++;
        }

        float average = channels == 0 ? 1f : support / channels;
        if (ecology.harmedByPollution)
            average *= 1f - Mathf.Clamp01(habitat.pollution);

        return Mathf.Clamp01(average);
    }

    PopulationStatus EvaluateStatus(SpeciesPopulation stock)
    {
        if (stock.population <= 0)
            return PopulationStatus.Extinct;

        int scarceAt = Mathf.Max(1, Mathf.RoundToInt(stock.maxPopulation * scarceFraction));
        return stock.population <= scarceAt ? PopulationStatus.Scarce : PopulationStatus.Healthy;
    }

    void Publish(RegenerationReport? regeneration)
    {
        WorldSnapshot snapshot = GetSnapshot();
        _environment?.ApplyEnvironment(snapshot);
        _ui?.PresentWorldState(snapshot);
        if (regeneration.HasValue)
            _ui?.OnRegenerationCompleted(regeneration.Value);
    }

    void NotifyPopulation(string speciesId)
    {
        if (!string.IsNullOrEmpty(speciesId))
            PopulationChanged?.Invoke(speciesId);
    }

    void EnsureContent()
    {
        bluefinTuna = SpeciesEcology.Ensure(bluefinTuna, "bluefin_tuna", 0.08f, coral: true);
        cod = SpeciesEcology.Ensure(cod, "cod", 0.16f, kelp: true);
        kingCrab = SpeciesEcology.Ensure(kingCrab, "king_crab", 0.1f, seagrass: true);
        mackerel = SpeciesEcology.Ensure(mackerel, "mackerel", 0.22f, kelp: true, seagrass: true);
        salmon = SpeciesEcology.Ensure(salmon, "salmon", 0.14f, kelp: true, seagrass: true);
        seaBass = SpeciesEcology.Ensure(seaBass, "sea_bass", 0.15f, kelp: true, seagrass: true);
        swordfish = SpeciesEcology.Ensure(swordfish, "swordfish", 0.07f, coral: true);

        if (regions == null || regions.Length == 0)
            regions = new[] { FishingRegion.CreateDefault() };

        for (int i = 0; i < regions.Length; i++)
        {
            if (regions[i] == null)
                regions[i] = FishingRegion.CreateDefault();

            regions[i].EnsureStocks();
        }

        if (FindRegion(activeRegionId) == null && regions.Length > 0)
            activeRegionId = regions[0].id;
    }

    void RebuildEcology()
    {
        _ecologyById.Clear();
        SpeciesEcology[] all = { bluefinTuna, cod, kingCrab, mackerel, salmon, seaBass, swordfish };
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !string.IsNullOrEmpty(all[i].speciesId))
                _ecologyById[all[i].speciesId] = all[i];
        }
    }

    SpeciesEcology FindEcology(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId))
            return null;

        if (_ecologyById.Count == 0)
            RebuildEcology();

        _ecologyById.TryGetValue(speciesId, out SpeciesEcology ecology);
        return ecology;
    }

    FishingRegion FindRegion(string regionId)
    {
        if (regions == null || string.IsNullOrEmpty(regionId))
        {
            WarnMissingRegion(regionId);
            return null;
        }

        for (int i = 0; i < regions.Length; i++)
        {
            if (regions[i] != null && regions[i].id == regionId)
                return regions[i];
        }

        WarnMissingRegion(regionId);
        return null;
    }

    SpeciesPopulation FindStock(string regionId, string speciesId)
    {
        return FindStock(FindRegion(regionId), speciesId);
    }

    static SpeciesPopulation FindStock(FishingRegion region, string speciesId)
    {
        if (region == null || string.IsNullOrEmpty(speciesId))
            return null;

        SpeciesPopulation[] stocks = region.AllStocks();
        for (int i = 0; i < stocks.Length; i++)
        {
            if (stocks[i] != null && stocks[i].speciesId == speciesId)
                return stocks[i];
        }

        return null;
    }

    void ResolveViews()
    {
        _ui = uiManager as IWorldUi;
        if (uiManager != null && _ui == null)
            Debug.LogWarning($"{nameof(WorldStateManager)} on {name}: {uiManager.GetType().Name} does not implement {nameof(IWorldUi)}.", this);

        _environment = environmentView as IWorldEnvironment;
        if (environmentView != null && _environment == null)
            Debug.LogWarning($"{nameof(WorldStateManager)} on {name}: {environmentView.GetType().Name} does not implement {nameof(IWorldEnvironment)}.", this);
    }

    void WarnMissingRegion(string regionId)
    {
        if (_warnedMissingRegion || string.IsNullOrEmpty(regionId))
            return;

        _warnedMissingRegion = true;
        Debug.LogWarning($"World State Manager has no fishing region named {regionId}.", this);
    }

    void SetState(WorldActivityState state)
    {
        if (CurrentState == state)
            return;

        CurrentState = state;
        StateChanged?.Invoke(state);
    }
}

public enum WorldActivityState
{
    Active,
    Regeneration
}

public enum PopulationStatus
{
    Healthy,
    Scarce,
    Extinct,
    Recovered
}

public enum HabitatFactor
{
    CoralReef,
    Kelp,
    Seagrass,
    Pollution
}

public struct RegionalPopulation
{
    public string RegionId;
    public string RegionName;
    public string SpeciesId;
    public int Population;
    public int MaxPopulation;
    public int Capacity;
    public PopulationStatus Status;
    public bool Available;
}

public struct WorldSnapshot
{
    public string ActiveRegionId;
    public RegionalPopulation[] Populations;
}

public struct SpeciesChange
{
    public string RegionId;
    public string SpeciesId;
    public PopulationStatus Previous;
    public PopulationStatus Current;
    public int Population;
    public int Growth;
    public bool Recovered;
    public bool BecameScarce;
    public bool WentExtinct;
}

public struct RegenerationReport
{
    public SpeciesChange[] Changes;
}

[Serializable]
public class SpeciesPopulation
{
    public string speciesId;
    [Min(0)] public int population = 10;
    [Min(0)] public int maxPopulation = 40;
    [NonSerialized] public PopulationStatus status;
}

[Serializable]
public class RegionHabitat
{
    [Range(0f, 1f)] public float coralReef = 0.6f;
    [Range(0f, 1f)] public float kelp = 0.6f;
    [Range(0f, 1f)] public float seagrass = 0.6f;
    [Range(0f, 1f)] public float pollution = 0.1f;
}

[Serializable]
public class FishingRegion
{
    public string id = "coast";
    public string displayName = "Coast";
    public RegionHabitat habitat = new RegionHabitat();
    public SpeciesPopulation bluefinTuna = new SpeciesPopulation { speciesId = "bluefin_tuna" };
    public SpeciesPopulation cod = new SpeciesPopulation { speciesId = "cod" };
    public SpeciesPopulation kingCrab = new SpeciesPopulation { speciesId = "king_crab" };
    public SpeciesPopulation mackerel = new SpeciesPopulation { speciesId = "mackerel" };
    public SpeciesPopulation salmon = new SpeciesPopulation { speciesId = "salmon" };
    public SpeciesPopulation seaBass = new SpeciesPopulation { speciesId = "sea_bass" };
    public SpeciesPopulation swordfish = new SpeciesPopulation { speciesId = "swordfish" };

    public static FishingRegion CreateDefault()
    {
        return new FishingRegion();
    }

    public void EnsureStocks()
    {
        if (string.IsNullOrWhiteSpace(id))
            id = "coast";

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = id;

        if (habitat == null)
            habitat = new RegionHabitat();

        bluefinTuna = EnsureStock(bluefinTuna, "bluefin_tuna");
        cod = EnsureStock(cod, "cod");
        kingCrab = EnsureStock(kingCrab, "king_crab");
        mackerel = EnsureStock(mackerel, "mackerel");
        salmon = EnsureStock(salmon, "salmon");
        seaBass = EnsureStock(seaBass, "sea_bass");
        swordfish = EnsureStock(swordfish, "swordfish");
    }

    public SpeciesPopulation[] AllStocks()
    {
        return new[] { bluefinTuna, cod, kingCrab, mackerel, salmon, seaBass, swordfish };
    }

    static SpeciesPopulation EnsureStock(SpeciesPopulation stock, string speciesId)
    {
        if (stock == null)
            stock = new SpeciesPopulation();

        if (string.IsNullOrWhiteSpace(stock.speciesId))
            stock.speciesId = speciesId;

        stock.population = Mathf.Max(0, stock.population);
        stock.maxPopulation = Mathf.Max(stock.population, stock.maxPopulation);
        return stock;
    }
}

[Serializable]
public class SpeciesEcology
{
    public string speciesId;
    [Min(0f)] public float baseReproductionRate = 0.1f;
    public bool respondsToCoral;
    public bool respondsToKelp;
    public bool respondsToSeagrass;
    public bool harmedByPollution = true;
    public bool canBeReintroduced;

    public static SpeciesEcology Create(string speciesId, float reproduction, bool coral = false, bool kelp = false, bool seagrass = false)
    {
        return new SpeciesEcology
        {
            speciesId = speciesId,
            baseReproductionRate = reproduction,
            respondsToCoral = coral,
            respondsToKelp = kelp,
            respondsToSeagrass = seagrass,
            harmedByPollution = true
        };
    }

    public static SpeciesEcology Ensure(SpeciesEcology ecology, string speciesId, float reproduction, bool coral = false, bool kelp = false, bool seagrass = false)
    {
        if (ecology == null)
            return Create(speciesId, reproduction, coral, kelp, seagrass);

        if (string.IsNullOrWhiteSpace(ecology.speciesId))
            ecology.speciesId = speciesId;

        if (ecology.baseReproductionRate < 0f)
            ecology.baseReproductionRate = reproduction;

        return ecology;
    }
}

public interface IWorldUi
{
    void PresentWorldState(WorldSnapshot snapshot);
    void OnRegenerationCompleted(RegenerationReport report);
}

public interface IWorldEnvironment
{
    void ApplyEnvironment(WorldSnapshot snapshot);
}
