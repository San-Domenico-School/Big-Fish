using UnityEngine;

public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    public int PlayerGold { get; private set; } = 150;
    public int KelpCount { get; private set; } = 0;
    public int CurrentFishCount { get; set; } = 5; // Placeholder starting catch

    [SerializeField] private int fishSellPrice = 20;
    [SerializeField] private int kelpBuyPrice = 50;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public bool BuyKelp()
    {
        if (PlayerGold >= kelpBuyPrice)
        {
            PlayerGold -= kelpBuyPrice;
            KelpCount++;
            Debug.Log($"Bought 1 Kelp! Remaining Gold: ${PlayerGold}, Kelp Total: {KelpCount}");
            return true;
        }
        
        Debug.Log("Not enough gold to buy Kelp!");
        return false;
    }

    public void SellAllFish()
    {
        if (CurrentFishCount > 0)
        {
            int earnings = CurrentFishCount * fishSellPrice;
            PlayerGold += earnings;
            Debug.Log($"Sold {CurrentFishCount} fish for ${earnings}! Total Gold: ${PlayerGold}");
            CurrentFishCount = 0;
        }
        else
        {
            Debug.Log("No fish to sell!");
        }
    }
}