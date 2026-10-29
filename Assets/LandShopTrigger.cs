using UnityEngine;
using UnityEngine.UIElements;

public class LandShopTrigger : MonoBehaviour
{
    public UIDocument uiDocument;
    
    private VisualElement buyContainer;
    private VisualElement sellContainer;

    private void Start()
    {
        if (uiDocument == null)
        {
            Debug.LogError("Please assign your UI Document to the LandShopTrigger script!");
            return;
        }

        var root = uiDocument.rootVisualElement;

        buyContainer = root.Q<VisualElement>("BuyContainer");
        sellContainer = root.Q<VisualElement>("SellContainer");

        // Hide shop menus at start
        SetShopVisible(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SetShopVisible(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SetShopVisible(false);
        }
    }

    private void SetShopVisible(bool isVisible)
    {
        DisplayStyle styleValue = isVisible ? DisplayStyle.Flex : DisplayStyle.None;

        if (buyContainer != null) 
            buyContainer.style.display = styleValue;
        
        if (sellContainer != null) 
            sellContainer.style.display = styleValue;
    }
}