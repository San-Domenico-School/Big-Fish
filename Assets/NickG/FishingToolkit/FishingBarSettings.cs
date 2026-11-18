using UnityEngine;

namespace BigFish.NickG.FishingToolkit
{
    [CreateAssetMenu(fileName = "FishingBarDefaults", menuName = "NickG/Fishing Bar Settings")]
    public sealed class FishingBarSettings : ScriptableObject
    {
        [Min(1f)] public float roundSeconds = 8f;
        [Range(2f, 20f)] public float tapsPerSecondAtRight = 6f;
        [Min(.25f)] public float holdSeconds = 2f;
        [Range(0f, 1f)] public float targetStart = .30f;
        [Range(0f, 1f)] public float targetEnd = .70f;
        public Color outsideColor = new Color32(187, 82, 97, 255);
        public Color targetColor = new Color32(56, 132, 113, 255);
        public Color timerColor = new Color32(43, 85, 98, 255);

        private void OnValidate()
        {
            roundSeconds = Safe(roundSeconds, 8f, 1f, 120f);
            tapsPerSecondAtRight = Safe(tapsPerSecondAtRight, 6f, 2f, 20f);
            holdSeconds = Safe(holdSeconds, 2f, .25f, roundSeconds);
            targetStart = Safe(targetStart, .3f, 0f, .95f);
            targetEnd = Safe(targetEnd, .7f, targetStart + .05f, 1f);
        }
        private static float Safe(float v, float fallback, float min, float max)
        { return float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, min, max); }
    }
}
