
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class BigFishFishing : MonoBehaviour
{
    [System.Serializable]
    public class FishData
    {
        public string fishName = "Blue Tang";
        public string speciesId;
        public GameObject fishPrefab;
        public int sellPrice = 25;
    }

    [Header("Catch Reward UI")]
    [SerializeField] private CatchRewardUI catchRewardUI;

    [Header("Economy")]
    [SerializeField] private SufianS_EconomyManager economy;

    [Header("Original Fish")]
    [SerializeField] private GameObject originalFish;

    [Header("Fish Available to Catch")]
    [SerializeField] private FishData[] fishAvailable;

    [Header("Testing")]
    [SerializeField] private bool testCatchOnSpace = true;

    private bool isFishing;
    private bool hasCaughtFish;

    private void Update()
    {
        if (!testCatchOnSpace || isFishing)
            return;

        bool spacePressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            spacePressed = Keyboard.current.spaceKey.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        spacePressed = Input.GetKeyDown(KeyCode.Space);
#endif

        if (spacePressed)
        {
            Debug.Log("Space pressed! Attempting to catch a fish.");
            TryCatchFish();
        }
    }

    public void StartFishing()
    {
        isFishing = true;
        hasCaughtFish = false;

        Debug.Log("Fishing started!");
    }

    public void TryCatchFish()
    {
        CompleteFishing(true);
    }

    public void CompleteFishing(bool success)
    {
        isFishing = false;

        if (!success)
        {
            Debug.Log("The fish got away!");
            return;
        }

        if (hasCaughtFish)
        {
            Debug.LogWarning("This catch has already been processed.");
            return;
        }

        if (catchRewardUI == null)
        {
            Debug.LogError(
                "BigFishFishing: Catch Reward UI is not assigned!"
            );
            return;
        }

        if (fishAvailable == null || fishAvailable.Length == 0)
        {
            Debug.LogError(
                "BigFishFishing: Add at least one fish to Fish Available."
            );
            return;
        }

        if (originalFish == null)
        {
            Debug.LogError(
                "BigFishFishing: Original Fish is not assigned!"
            );
            return;
        }

        FishData caughtFish = GetRandomFish();

        if (caughtFish == null)
        {
            Debug.LogError(
                "BigFishFishing: No fish has a valid prefab assigned!"
            );
            return;
        }

        hasCaughtFish = true;

        int sellPrice = QuoteSellPrice(caughtFish);

        Debug.Log(
            "Caught " + caughtFish.fishName +
            " worth $" + sellPrice
        );

        catchRewardUI.ShowCatch(
            originalFish,
            caughtFish.fishName,
            sellPrice
        );
    }

    private int QuoteSellPrice(FishData caughtFish)
    {
        if (economy == null)
            economy = FindAnyObjectByType<SufianS_EconomyManager>();

        if (economy == null || string.IsNullOrEmpty(caughtFish.speciesId))
        {
            Debug.LogWarning(
                "BigFishFishing: Economy sell price is unavailable. Using the fallback price."
            );
            return caughtFish.sellPrice;
        }

        SpeciesQuote[] species = economy.GetMarketReport().Species;

        if (species != null)
        {
            for (int i = 0; i < species.Length; i++)
            {
                if (species[i].Id == caughtFish.speciesId)
                    return species[i].SellPrice;
            }
        }

        Debug.LogWarning(
            "BigFishFishing: No economy quote for " + caughtFish.speciesId + ". Using the fallback price."
        );
        return caughtFish.sellPrice;
    }

    private FishData GetRandomFish()
    {
        int validCount = 0;

        foreach (FishData fish in fishAvailable)
        {
            if (fish != null && fish.fishPrefab != null)
                validCount++;
        }

        if (validCount == 0)
            return null;

        int chosenIndex = Random.Range(0, validCount);

        foreach (FishData fish in fishAvailable)
        {
            if (fish != null && fish.fishPrefab != null)
            {
                if (chosenIndex == 0)
                    return fish;

                chosenIndex--;
            }
        }

        return null;
    }
}
