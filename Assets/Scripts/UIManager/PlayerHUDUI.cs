using UnityEngine;
using TMPro; // Make sure to install TextMeshPro package if prompted

public class PlayerHUDUI : MonoBehaviour
{
    [Header("Player Data Displays")]
    [SerializeField] private TextMeshProUGUI moneyText;
    [SerializeField] private TextMeshProUGUI fishCapacityText;
    [SerializeField] private TextMeshProUGUI rankText;

    [Header("World Data Displays")]
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI dayText;
    [SerializeField] private TextMeshProUGUI weatherText;

    private void Start()
    {
        // Test values based on GDD layout
        UpdateHUD(150, 0, 10, 1, "10:15 AM", 1, "Sunny");
    }
    
    // Call this method whenever player stats or world time update
    public void UpdateHUD(int currentMoney, int fishCount, int maxCapacity, int playerRank, string currentTime, int dayCount, string weather)
    {
        if (moneyText != null) moneyText.text = $"${currentMoney}";
        if (fishCapacityText != null) fishCapacityText.text = $"Fish: {fishCount}/{maxCapacity}";
        if (rankText != null) rankText.text = $"Rank {playerRank}";
        
        if (timeText != null) timeText.text = currentTime;
        if (dayText != null) dayText.text = $"Day {dayCount}";
        if (weatherText != null) weatherText.text = weather;
    }
}