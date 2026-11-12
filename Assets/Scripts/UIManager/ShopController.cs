using UnityEngine;

public class ShopController : MonoBehaviour
{
    public void OnClickSellFish()
    {
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.SellAllFish();
        }
    }

    public void OnClickBuyKelp()
    {
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.BuyKelp();
        }
    }

    public void OnClickExitShop()
    {
        if (ShopUISlider.Instance != null)
        {
            ShopUISlider.Instance.CloseShopUI();
        }
    }
}