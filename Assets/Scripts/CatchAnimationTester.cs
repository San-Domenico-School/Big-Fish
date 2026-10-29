
using UnityEngine;

public class CatchAnimationTester : MonoBehaviour
{
    [SerializeField] private CatchRewardUI catchRewardUI;
    [SerializeField] private GameObject fishPrefab;

    [ContextMenu("Test Catch Animation")]
    public void TestCatch()
    {
        if (catchRewardUI == null || fishPrefab == null)
        {
            Debug.LogWarning(
                "Assign CatchRewardUI and Fish Prefab first."
            );
            return;
        }

        catchRewardUI.ShowCatch(
            fishPrefab,
            "Blue Tang",
            25
        );
    }
}
