
using UnityEngine;

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

    [Header("Fish Available to Catch")]
    [SerializeField] private FishData[] fishAvailable;

    [Header("Fishing Settings")]
    [SerializeField] private KeyCode catchKey = KeyCode.Space;
    [SerializeField] private bool testCatchOnSpace = true;

    private bool isFishing;
    private bool hasCaughtFish;

    private void Update()
    {
        // Simple test: press Space to catch a random configured fish.
        if (testCatchOnSpace &&
            Input.GetKeyDown(catchKey) &&
            !isFishing)
        {
            TryCatchFish();
        }
    }

    public void StartFishing()
    {
        isFishing = true;
        hasCaughtFish = false;

        Debug.Log("Fishing started! Wait for a catch.");
    }

    public void TryCatchFish()
    {
        if (isFishing)
        {
            CompleteFishing(true);
        }
        else
        {
            // Allows Space to test a catch without a fishing minigame.
            CompleteFishing(true);
        }
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
                "BigFishFishing: Assign CatchRewardUI in the Inspector."
            );
            return;
        }

        if (fishAvailable == null || fishAvailable.Length == 0)
        {
            Debug.LogError(
                "BigFishFishing: Add at least one fish in Fish Available."
            );
            return;
        }

        // Choose a random fish with a valid prefab.
        FishData caughtFish = GetRandomFish();

        if (caughtFish == null)
        {
            Debug.LogError(
                "BigFishFishing: No fish with an assigned prefab was found."
            );
            return;
        }

        hasCaughtFish = true;

        // Show the 3D fish, spin animation, name, and sell price.
        catchRewardUI.ShowCatch(
            caughtFish.fishPrefab,
            caughtFish.fishName,
            caughtFish.sellPrice
        );

        Debug.Log(
            "Caught " + caughtFish.fishName +
            " worth $" + caughtFish.sellPrice + "!"
        );
    }

    private FishData GetRandomFish()
    {
        int validCount = 0;

        foreach (FishData fish in fishAvailable)
        {
            if (fish != null && fish.fishPrefab != null)
            {
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return null;
        }

        int chosenIndex = Random.Range(0, validCount);

        foreach (FishData fish in fishAvailable)
        {
            if (fish != null && fish.fishPrefab != null)
            {
                if (chosenIndex == 0)
                {
                    return fish;
                }

                chosenIndex--;
            }
        }

        return null;
    }
}
