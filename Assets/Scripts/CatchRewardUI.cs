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

    [Header("3D Preview & Launch Settings")]
    [SerializeField] private Transform fishAnchor;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private float delayBeforeLaunch = 1.0f; // Delay after triggering before fish appears
    [SerializeField] private float launchDuration = 0.5f; // Time spent shooting to center
    [SerializeField] private Vector3 startLaunchOffset = new Vector3(0f, -5f, 0f); // Spawns below center view

    [Header("Particles")]
    [SerializeField] private ParticleSystem catchBurst1;
    [SerializeField] private ParticleSystem catchBurst2;
    [SerializeField] private ParticleSystem catchBurst3;

    [Header("Animation")]
    [SerializeField] private float entranceDuration = 0.65f;
    [SerializeField] private float entranceSpinDegrees = 540f;
    [SerializeField] private float idleSpinDegreesPerSecond = 40f;
    [SerializeField] private float bobPixels = 10f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float holdDuration = 2.4f;
    [SerializeField] private float fadeDuration = 0.45f;
    [SerializeField] private Vector2 startOffset = new Vector2(280f, -170f);

    private GameObject currentFish;
    private Coroutine animationRoutine;
    private Vector2 finalPosition;
    private Quaternion initialFishRotation;
    private Quaternion initialGlowRotation;

    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private Vector3 originalLocalScale;

    private void Awake()
    {
        if (fishDisplayRect != null)
            finalPosition = fishDisplayRect.anchoredPosition;

        if (glowRays != null)
        {
            initialGlowRotation = glowRays.rectTransform.localRotation;
        }

        if (catchPopup != null)
            catchPopup.SetActive(false);

        if (previewCamera != null)
            previewCamera.enabled = false;
    }

    public void ShowCatch(
        GameObject originalFish,
        string fishName,
        int sellPrice)
    {
        if (originalFish == null)
        {
            Debug.LogWarning("CatchRewardUI: Original fish is missing.");
            return;
        }

        if (fishAnchor == null)
        {
            Debug.LogError("CatchRewardUI: Fish Anchor is not assigned.");
            return;
        }

        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
            RestoreFish();
        }

        StopAllParticles(true);

        currentFish = originalFish;

        originalParent = currentFish.transform.parent;
        originalLocalPosition = currentFish.transform.localPosition;
        originalLocalRotation = currentFish.transform.localRotation;
        originalLocalScale = currentFish.transform.localScale;

        currentFish.transform.SetParent(fishAnchor, false);
        currentFish.transform.localPosition = startLaunchOffset;
        currentFish.transform.localRotation = Quaternion.Euler(-45f, -90f, 0f);

        initialFishRotation = Quaternion.Euler(0f, -90f, 0f);

        catchName.text = fishName;
        catchPrice.text = "Value: $" + sellPrice;

        popupGroup.alpha = 0f;
        fishDisplayRect.anchoredPosition = finalPosition + startOffset;
        fishDisplayRect.localScale = Vector3.one * 0.25f;

        if (glowRays != null)
        {
            glowRays.color = new Color(1f, 0.85f, 0.3f, 0f);
            glowRays.rectTransform.localRotation = initialGlowRotation;
        }

        animationRoutine = StartCoroutine(AnimateCatchSequence());
    }

    private IEnumerator AnimateCatchSequence()
    {
        // 1. Wait configured delay after input
        yield return new WaitForSeconds(delayBeforeLaunch);

        // Turn on UI popup and Camera after delay
        if (catchPopup != null)
            catchPopup.SetActive(true);

        if (previewCamera != null)
            previewCamera.enabled = true;

        // 2. Shoot fish up from bottom to center
        float launchElapsed = 0f;
        while (launchElapsed < launchDuration)
        {
            launchElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(launchElapsed / launchDuration);
            float easedT = 1f - Mathf.Pow(1f - t, 3f); // Fast launch easing

            currentFish.transform.localPosition = Vector3.Lerp(startLaunchOffset, Vector3.zero, easedT);
            currentFish.transform.localRotation = Quaternion.Slerp(
                Quaternion.Euler(-45f, -90f, 0f),
                initialFishRotation,
                easedT
            );

            yield return null;
        }

        currentFish.transform.localPosition = Vector3.zero;
        currentFish.transform.localRotation = initialFishRotation;

        // 3. Trigger totem particles once fish hits center screen
        PlayAllParticles();

        // 4. Main spin, hold, and fade animation
        float elapsed = 0f;
        float totalDuration = entranceDuration + holdDuration + fadeDuration;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;

            float enter = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, entranceDuration));
            float eased = 1f - Mathf.Pow(1f - enter, 3f);

            fishDisplayRect.anchoredPosition = Vector2.Lerp(
                finalPosition + startOffset,
                finalPosition,
                eased
            );

            fishDisplayRect.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, eased);

            float fadeIn = Mathf.Clamp01(elapsed / 0.2f);
            float fadeOutStart = entranceDuration + holdDuration;
            float fadeOut = Mathf.Clamp01((elapsed - fadeOutStart) / Mathf.Max(0.01f, fadeDuration));

            popupGroup.alpha = Mathf.Min(fadeIn, 1f - fadeOut);

            if (currentFish != null)
            {
                if (elapsed <= entranceDuration)
                {
                    currentFish.transform.localRotation =
                        initialFishRotation * Quaternion.Euler(0f, entranceSpinDegrees * eased, 0f);
                }
                else
                {
                    float idleTime = elapsed - entranceDuration;
                    currentFish.transform.localRotation =
                        initialFishRotation * Quaternion.Euler(0f, entranceSpinDegrees + idleSpinDegreesPerSecond * idleTime, 0f);
                }
            }

            float bob = Mathf.Sin(elapsed * bobSpeed) * bobPixels;
            float bobWeight = Mathf.Clamp01((elapsed - entranceDuration) / 0.25f);
            fishDisplayRect.anchoredPosition += Vector2.up * bob * bobWeight;

            if (glowRays != null)
            {
                Color color = glowRays.color;
                color.a = 0.8f * (1f - fadeOut);
                glowRays.color = color;
                glowRays.rectTransform.Rotate(0f, 0f, 8f * Time.deltaTime);
            }

            yield return null;
        }

        popupGroup.alpha = 0f;
        catchPopup.SetActive(false);

        if (previewCamera != null)
            previewCamera.enabled = false;

        StopAllParticles(false);
        RestoreFish();

        animationRoutine = null;
    }

    private void PlayAllParticles()
    {
        if (catchBurst1 != null) catchBurst1.Play();
        if (catchBurst2 != null) catchBurst2.Play();
        if (catchBurst3 != null) catchBurst3.Play();
    }

    private void StopAllParticles(bool clearImmediately)
    {
        ParticleSystemStopBehavior stopBehavior = clearImmediately
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        if (catchBurst1 != null) catchBurst1.Stop(true, stopBehavior);
        if (catchBurst2 != null) catchBurst2.Stop(true, stopBehavior);
        if (catchBurst3 != null) catchBurst3.Stop(true, stopBehavior);
    }

    private void RestoreFish()
    {
        if (currentFish == null)
            return;

        currentFish.transform.SetParent(originalParent, false);
        currentFish.transform.localPosition = originalLocalPosition;
        currentFish.transform.localRotation = originalLocalRotation;
        currentFish.transform.localScale = originalLocalScale;

        currentFish = null;
    }
}