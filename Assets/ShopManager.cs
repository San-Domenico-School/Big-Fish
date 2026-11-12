using UnityEngine;
using UnityEngine.UI;

public class ShopManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject shopPanel;
    public Button buyButton;
    public Button sellButton;

    private void Start()
    {
        // Add listeners to buttons
        if (buyButton != null) buyButton.onClick.AddListener(OnBuyClicked);
        if (sellButton != null) sellButton.onClick.AddListener(OnSellClicked);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if the entering object is the player
        if (other.CompareTag("Player"))
        {
            shopPanel.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            shopPanel.SetActive(false);
        }
    }

    private void OnBuyClicked()
    {
        Debug.Log("Player clicked BUY");
        // Add buy logic here
    }

    private void OnSellClicked()
    {
        Debug.Log("Player clicked SELL");
        // Add sell logic here
    }
}