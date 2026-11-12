using System.Collections;
using UnityEngine;

public class ShopUISlider : MonoBehaviour
{
    public static ShopUISlider Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private CanvasGroup shopPanelGroup; // Drag Shop_Panel here
    [SerializeField] private GameObject openShopButton;    // Drag OpenShopButton here

    [Header("Fade Settings")]
    [SerializeField] private float fadeDuration = 0.5f;

    private bool isDocked = false;
    private bool isAnimating = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    private void Start()
    {
        // Start in the ocean: hide the button and make the shop panel invisible
        if (openShopButton != null) openShopButton.SetActive(false);
        if (shopPanelGroup != null)
        {
            shopPanelGroup.alpha = 0f;
            shopPanelGroup.interactable = false;
            shopPanelGroup.blocksRaycasts = false;
            shopPanelGroup.gameObject.SetActive(false);
        }
    }

    // Call this with true when you arrive at the dock, and false when leaving for the ocean
    public void SetDockedState(bool docked)
    {
        isDocked = docked;
        if (openShopButton != null)
        {
            openShopButton.SetActive(isDocked);
        }
    }

    // Hook this to your OpenShopButton OnClick()
    public void OpenShopUI()
    {
        if (isDocked && !isAnimating)
        {
            StartCoroutine(FadePanelRoutine(true));
        }
    }

    // Hook this to your ExitShopButton OnClick()
    public void CloseShopUI()
    {
        if (!isAnimating)
        {
            StartCoroutine(FadePanelRoutine(false));
        }
    }

    private IEnumerator FadePanelRoutine(bool fadingIn)
    {
        isAnimating = true;

        if (fadingIn)
        {
            if (openShopButton != null) openShopButton.SetActive(false);
            shopPanelGroup.gameObject.SetActive(true);
        }
        else
        {
            // Disable button clicks immediately while fading out
            shopPanelGroup.interactable = false;
            shopPanelGroup.blocksRaycasts = false;
        }

        float startAlpha = shopPanelGroup.alpha;
        float targetAlpha = fadingIn ? 1f : 0f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / fadeDuration);
            shopPanelGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }

        shopPanelGroup.alpha = targetAlpha;
        isAnimating = false;

        if (fadingIn)
        {
            // Enable clicking on shop buttons once fully visible
            shopPanelGroup.interactable = true;
            shopPanelGroup.blocksRaycasts = true;
        }
        else
        {
            shopPanelGroup.gameObject.SetActive(false);
            if (openShopButton != null) openShopButton.SetActive(isDocked);
        }
    }
}