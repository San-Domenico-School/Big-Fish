
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CatchRewardUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject catchPopup;
    [SerializeField] private CanvasGroup popupGroup;
    [SerializeField] private RectTransform fishDisplayRect;
    [SerializeField] private TMP_Text catchName;
    [SerializeField] private TMP_Text catchPrice;
    [SerializeField] private Image glowRays;

    [Header("3D Preview")]
    [SerializeField] private Transform fishAnchor;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private ParticleSystem catchBurst;

    [Header("Animation")]
    [SerializeField] private float entranceDuration = 0.65f;
    [SerializeField] private float entranceSpinDegrees = 540f;
    [SerializeField] private float idleSpinDegreesPerSecond = 40f;
    [SerializeField] private float bobPixels = 10f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float holdDuration = 2.4f;
    [SerializeField] private float fadeDuration = 0.45f;
    [SerializeField] private Vector2 startOffset =
        new Vector2(280f, -170f);

    private GameObject currentFish;
    private Coroutine animationRoutine;
    private Vector2 finalPosition;
    private Quaternion initialFishRotation;
    private Quaternion initialGlowRotation;

    private void Awake()
    {
        finalPosition = fishDisplayRect.anchoredPosition;

        if (glowRays != null)
        {
            initialGlowRotation =
                glowRays.rectTransform.localRotation;
        }

        catchPopup.SetActive(false);

        if (previewCamera != null)
            previewCamera.enabled = false;
    }

    public void ShowCatch(
        GameObject fishPrefab,
        string fishName,
        int sellPrice)
    {
        if (fishPrefab == null)
        {
            Debug.LogWarning("CatchRewardUI: Fish prefab is missing.");
            return;
        }

        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        if (currentFish != null)
            Destroy(currentFish);

        if (catchBurst != null)
        {
            catchBurst.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }

        currentFish = Instantiate(fishPrefab, fishAnchor);
        currentFish.transform.localPosition = Vector3.zero;
        currentFish.transform.localRotation =
            Quaternion.Euler(0f, -90f, 0f);

        initialFishRotation =
            currentFish.transform.localRotation;

        catchName.text = fishName;
        catchPrice.text = "Value: $" + sellPrice;

        catchPopup.SetActive(true);

        if (previewCamera != null)
            previewCamera.enabled = true;

        popupGroup.alpha = 0f;
        fishDisplayRect.anchoredPosition =
            finalPosition + startOffset;
        fishDisplayRect.localScale = Vector3.one * 0.25f;

        if (glowRays != null)
        {
            glowRays.color = new Color(
                1f, 0.85f, 0.3f, 0f
            );

            glowRays.rectTransform.localRotation =
                initialGlowRotation;
        }

        if (catchBurst != null)
            catchBurst.Play();

        animationRoutine = StartCoroutine(AnimateCatch());
    }

    private IEnumerator AnimateCatch()
    {
        float elapsed = 0f;
        float totalDuration =
            entranceDuration + holdDuration + fadeDuration;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;

            float enter = Mathf.Clamp01(
                elapsed / entranceDuration
            );

            // Ease out for a smooth arrival.
            float eased = 1f - Mathf.Pow(1f - enter, 3f);

            fishDisplayRect.anchoredPosition = Vector2.Lerp(
                finalPosition + startOffset,
                finalPosition,
                eased
            );

            fishDisplayRect.localScale =
                Vector3.one * Mathf.Lerp(0.25f, 1f, eased);

            // Fade in, hold, then fade out.
            float fadeIn = Mathf.Clamp01(elapsed / 0.2f);
            float fadeOutStart =
                entranceDuration + holdDuration;

            float fadeOut = Mathf.Clamp01(
                (elapsed - fadeOutStart) /
                Mathf.Max(0.01f, fadeDuration)
            );

            popupGroup.alpha = Mathf.Min(
                fadeIn, 1f - fadeOut
            );

            // Fast spin on entry, slow spin while displayed.
            if (currentFish != null)
            {
                if (elapsed <= entranceDuration)
                {
                    currentFish.transform.localRotation =
                        initialFishRotation *
                        Quaternion.Euler(
                            0f,
                            entranceSpinDegrees * eased,
                            0f
                        );
                }
                else
                {
                    float idleTime =
                        elapsed - entranceDuration;

                    currentFish.transform.localRotation =
                        initialFishRotation *
                        Quaternion.Euler(
                            0f,
                            entranceSpinDegrees +
                            idleSpinDegreesPerSecond * idleTime,
                            0f
                        );
                }
            }

            // Small up-and-down movement of the UI preview.
            float bob = Mathf.Sin(elapsed * bobSpeed) * bobPixels;
            float bobWeight = Mathf.Clamp01(
                (elapsed - entranceDuration) / 0.25f
            );

            fishDisplayRect.anchoredPosition +=
                Vector2.up * bob * bobWeight;

            // Bright glow that fades with the popup.
            if (glowRays != null)
            {
                Color color = glowRays.color;
                color.a = 0.8f * (1f - fadeOut);
                glowRays.color = color;

                glowRays.rectTransform.Rotate(
                    0f, 0f, 8f * Time.deltaTime
                );
            }

            yield return null;
        }

        popupGroup.alpha = 0f;
        catchPopup.SetActive(false);

        if (previewCamera != null)
            previewCamera.enabled = false;

        if (currentFish != null)
            Destroy(currentFish);

        animationRoutine = null;
    }
}
