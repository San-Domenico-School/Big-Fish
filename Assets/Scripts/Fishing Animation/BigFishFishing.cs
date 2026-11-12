
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
        public GameObject fishPrefab;
        public int sellPrice = 25;
    }

    [Header("Catch Reward UI")]
    [SerializeField] private CatchRewardUI catchRewardUI;

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

        Debug.Log(
            "Caught " + caughtFish.fishName +
            " worth $" + caughtFish.sellPrice
        );

        catchRewardUI.ShowCatch(
            originalFish,
            caughtFish.fishName,
            caughtFish.sellPrice
        );
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
