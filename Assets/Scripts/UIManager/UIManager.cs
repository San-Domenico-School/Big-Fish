using UnityEngine;

public enum UIState
{
    PlayerHUD,
    Shop,
    Fishing
}

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Panels")]
    [SerializeField] private GameObject playerHUDPanel;
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private GameObject fishingPanel;

    [Header("Cameras")]
    [SerializeField] private GameObject boatCamera;
    [SerializeField] private GameObject shopCamera;
    [SerializeField] private GameObject fishingCamera;

    private UIState currentState;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Start game in Player HUD state
        SetState(UIState.PlayerHUD);
    }

    public void SetState(UIState newState)
    {
        currentState = newState;

        // Disable all UI panels & cameras first
        DisableAll();

        // Enable active UI panel and camera based on state
        switch (currentState)
        {
            case UIState.PlayerHUD:
                if (playerHUDPanel != null) playerHUDPanel.SetActive(true);
                if (boatCamera != null) boatCamera.SetActive(true);
                break;

            case UIState.Shop:
                if (shopPanel != null) shopPanel.SetActive(true);
                if (shopCamera != null) shopCamera.SetActive(true);
                break;

            case UIState.Fishing:
                if (fishingPanel != null) fishingPanel.SetActive(true);
                if (fishingCamera != null) fishingCamera.SetActive(true);
                break;
        }
    }

    private void DisableAll()
    {
        if (playerHUDPanel != null) playerHUDPanel.SetActive(false);
        if (shopPanel != null) shopPanel.SetActive(false);
        if (fishingPanel != null) fishingPanel.SetActive(false);

        if (boatCamera != null) boatCamera.SetActive(false);
        if (shopCamera != null) shopCamera.SetActive(false);
        if (fishingCamera != null) fishingCamera.SetActive(false);
    }

    public UIState GetCurrentState() => currentState;
}