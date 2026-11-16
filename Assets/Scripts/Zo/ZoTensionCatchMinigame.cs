using System;
using UnityEngine;
using UnityEngine.UI;

namespace BigFish
{
    /// <summary>
    /// Strike + line-tension catch minigame (ported from the Desktop/CODE Big Fish demo:
    /// FishingController's Bite/Reeling states and GameHUD's fishing overlay + catch card).
    ///
    ///   1. BITE   - a "!" ring shrinks. Press SPACE before it runs out to strike.
    ///   2. REEL   - HOLD SPACE to raise line tension, let go to ease off.
    ///               Keep the needle inside the green band to reel the fish in (LANDED bar).
    ///               Too much tension builds line stress - full stress and the line parts.
    ///               Slack line lets the fish take back what you gained.
    ///   3. CATCH  - catch card pops up with name, size and grade.
    ///
    /// Drop-in replacement for any ZoCatchMinigame: assign it to ZoFishingModeController's Catch Minigame slot.
    /// </summary>
    public class ZoTensionCatchMinigame : ZoCatchMinigame
    {
        [Serializable]
        public class FishTuning
        {
            [Tooltip("Matches the catch table id in FishingSystem.")]
            public string id = "fish";
            [Range(0f, 1f)] public float difficulty = 0.4f;
            public float minCm = 20f;
            public float maxCm = 60f;
        }

        [Header("Fish")]
        [SerializeField] FishTuning[] fishTuning =
        {
            new FishTuning { id = "clownfish", difficulty = 0.15f, minCm = 6f,   maxCm = 11f },
            new FishTuning { id = "blue_tang", difficulty = 0.30f, minCm = 15f,  maxCm = 30f },
            new FishTuning { id = "tuna",      difficulty = 0.76f, minCm = 120f, maxCm = 250f },
            new FishTuning { id = "old_boot",  difficulty = 0.05f, minCm = 28f,  maxCm = 32f },
        };
        [Range(0f, 1f)] [SerializeField] float defaultDifficulty = 0.4f;

        [Header("Rod (the CODE demo's rod upgrades: Bamboo 0.26, Carbon 0.38, Deepwater 0.50)")]
        [Tooltip("Width of the green tension band. Wider = easier.")]
        [SerializeField] float rodBandWidth = 0.38f;
        [Tooltip("How fast holding reels. CODE demo: 0.55 (bamboo) to 0.95 (deepwater).")]
        [SerializeField] float reelRate = 0.75f;

        [Header("Bite Window")]
        [Tooltip("Seconds to strike for the easiest / hardest fish.")]
        [SerializeField] Vector2 biteWindow = new Vector2(1.05f, 0.55f);

        // ---- state, readable by other scripts ----
        public bool IsBitePhase => IsRunning && _phase == Phase.Bite;
        public bool IsReeling => IsRunning && _phase == Phase.Reeling;
        public float Tension { get; private set; }
        public float Progress { get; private set; }
        public float Stress { get; private set; }
        public float BandLow { get; private set; }
        public float BandHigh { get; private set; }
        public bool InBand => Tension >= BandLow && Tension <= BandHigh;
        public float BiteCountdown01 { get; private set; }

        public override string ResultMessage => _resultMessage;
        public override bool ShowsOwnCatchCard => true;

        enum Phase { Bite, Reeling }

        Phase _phase;
        float _timer;
        float _window;
        float _difficulty;
        float _maturity;
        float _cm;
        float _struggle;
        float _strugglePhase;
        float _nextSurge;
        string _resultMessage;

        // ---- UI ----
        Canvas _canvas;
        RectTransform _biteRing;
        Image _biteRingImage;
        Text _biteText;
        RectTransform _reelCard;
        Text _reelSpecies, _reelHint;
        Image _progressFill, _stressFill, _tensionFill, _tensionBand, _tensionNeedle;
        RectTransform _catchCard;
        CanvasGroup _catchGroup;
        Text _catchName, _catchStats, _catchValue;
        float _catchTime = -99f;

        void Awake()
        {
            BuildUi();
        }

        // ==============================
        // MINIGAME FLOW
        // ==============================

        protected override void OnBegin(CatchInfo fish)
        {
            FishTuning t = FindTuning(fish.Id);
            _difficulty = t != null ? t.difficulty : defaultDifficulty;
            _maturity = UnityEngine.Random.value;
            _cm = t != null ? Mathf.Lerp(t.minCm, t.maxCm, _maturity) : Mathf.Lerp(20f, 60f, _maturity);
            _resultMessage = null;

            _phase = Phase.Bite;
            _window = Mathf.Lerp(biteWindow.x, biteWindow.y, _difficulty);
            _timer = _window;
            BiteCountdown01 = 1f;
            Tension = Progress = Stress = 0f;

            _biteRing.gameObject.SetActive(true);
            _reelCard.gameObject.SetActive(false);
        }

        protected override void OnEnd()
        {
            if (_biteRing != null) _biteRing.gameObject.SetActive(false);
            if (_reelCard != null) _reelCard.gameObject.SetActive(false);
        }

        public override void Tick(float dt, bool pressed, bool held)
        {
            if (!IsRunning)
                return;

            if (_phase == Phase.Bite)
                TickBite(dt, pressed);
            else
                TickReeling(dt, held);
        }

        void TickBite(float dt, bool pressed)
        {
            _timer -= dt;
            BiteCountdown01 = Mathf.Clamp01(_timer / _window);
            UpdateBiteUi();

            if (pressed)
            {
                StartFight();
                return;
            }

            if (_timer <= 0f)
            {
                _resultMessage = $"{CurrentFish.DisplayName} spat the hook. Too slow.";
                Finish(false);
            }
        }

        void StartFight()
        {
            _phase = Phase.Reeling;
            Progress = 0.06f;
            Tension = 0.30f;
            Stress = 0f;
            _struggle = 0f;
            _strugglePhase = UnityEngine.Random.value * 6f;
            _nextSurge = UnityEngine.Random.Range(0.8f, 1.8f);

            // A wider safe band is exactly what the rod upgrades buy you.
            float width = rodBandWidth * Mathf.Lerp(1.1f, 0.72f, _difficulty);
            BandLow = Mathf.Lerp(0.20f, 0.34f, _difficulty);
            BandHigh = Mathf.Min(0.96f, BandLow + width);

            _biteRing.gameObject.SetActive(false);
            _reelCard.gameObject.SetActive(true);
            UpdateReelUi();
        }

        void TickReeling(float dt, bool held)
        {
            float difficulty = _difficulty * Mathf.Lerp(0.72f, 1.15f, _maturity);

            // The fish fights in waves, with occasional hard runs.
            _strugglePhase += dt * Mathf.Lerp(1.6f, 3.4f, difficulty);
            _nextSurge -= dt;
            if (_nextSurge <= 0f)
            {
                _struggle += Mathf.Lerp(0.18f, 0.48f, difficulty);
                _nextSurge = UnityEngine.Random.Range(1.1f, 2.6f) * Mathf.Lerp(1.4f, 0.7f, difficulty);
            }
            _struggle = Mathf.Lerp(_struggle, 0f, dt * 1.3f);
            float fight = (Mathf.Sin(_strugglePhase) * 0.5f + 0.5f) * Mathf.Lerp(0.10f, 0.34f, difficulty) + _struggle;

            if (held)
                Tension += (reelRate * 0.85f + fight) * dt;
            else
                Tension -= (0.85f - fight * 0.4f) * dt;
            Tension = Mathf.Clamp(Tension, 0f, 1.25f);

            if (InBand)
            {
                // a small fish comes in quickly; a big one is a long haul
                Progress += reelRate * Mathf.Lerp(0.55f, 0.22f, difficulty) * dt;
                Stress = Mathf.Max(0f, Stress - dt * 0.55f);
            }
            else if (Tension > BandHigh)
            {
                Stress += (Tension - BandHigh) * 2.6f * dt;
            }
            else
            {
                // slack line: the fish takes back what you gained
                Progress -= 0.10f * dt;
                Stress = Mathf.Max(0f, Stress - dt * 0.35f);
            }

            Progress = Mathf.Clamp01(Progress);
            Stress = Mathf.Clamp01(Stress);
            UpdateReelUi();

            if (Stress >= 1f)
            {
                _resultMessage = $"Line parted. The {CurrentFish.DisplayName.ToLower()} is gone.";
                Finish(false);
                return;
            }

            if (Progress >= 1f)
            {
                ShowCatchCard();
                Finish(true);
            }
        }

        FishTuning FindTuning(string id)
        {
            if (fishTuning == null)
                return null;
            foreach (var t in fishTuning)
                if (t != null && t.id == id)
                    return t;
            return null;
        }

        // ==============================
        // UI UPDATE (from GameHUD.UpdateFishing / UpdateCatchCard)
        // ==============================

        void UpdateBiteUi()
        {
            float s = Mathf.Lerp(52f, 132f, BiteCountdown01);
            _biteRing.sizeDelta = new Vector2(s, s);
            _biteText.fontSize = Mathf.RoundToInt(Mathf.Lerp(34f, 64f, BiteCountdown01));
            _biteRingImage.color = Color.Lerp(ZoUIKit.Bad, new Color(1f, 0.80f, 0.33f), BiteCountdown01);
        }

        void UpdateReelUi()
        {
            _reelSpecies.text = $"{CurrentFish.DisplayName.ToUpper()}   ~{_cm:0} cm";
            _progressFill.fillAmount = Progress;
            _stressFill.fillAmount = Stress;

            float lo = Mathf.Clamp01(BandLow), hi = Mathf.Clamp01(BandHigh);
            _tensionBand.rectTransform.anchorMin = new Vector2(lo, 0f);
            _tensionBand.rectTransform.anchorMax = new Vector2(hi, 1f);
            _tensionBand.color = InBand
                ? new Color(0.35f, 0.92f, 0.48f, 0.55f)
                : new Color(0.30f, 0.80f, 0.45f, 0.30f);

            float ten = Mathf.Clamp01(Tension);
            _tensionFill.rectTransform.anchorMax = new Vector2(ten, 1f);
            _tensionFill.color = Tension > hi
                ? Color.Lerp(ZoUIKit.Warn, ZoUIKit.Bad, Stress)
                : new Color(0.36f, 0.76f, 0.80f, 0.60f);

            _tensionNeedle.rectTransform.anchorMin = new Vector2(ten, 0f);
            _tensionNeedle.rectTransform.anchorMax = new Vector2(ten, 1f);
            _tensionNeedle.color = InBand ? Color.white : ZoUIKit.Bad;

            _reelHint.text = Stress > 0.55f
                ? "EASE OFF - the line is about to part"
                : "HOLD [SPACE] to reel  -  keep the needle inside the green";
            _reelHint.color = Stress > 0.55f ? ZoUIKit.Bad : ZoUIKit.Muted;
        }

        void ShowCatchCard()
        {
            _catchName.text = CurrentFish.DisplayName;
            string grade = _maturity < 0.35f ? "JUVENILE" : _maturity < 0.7f ? "GROWN" : "PRIME";
            _catchStats.text = $"{_cm:0} cm   -   {grade}";
            _catchStats.color = _maturity < 0.35f ? ZoUIKit.Bad : _maturity < 0.7f ? ZoUIKit.Muted : ZoUIKit.Good;
            _catchValue.text = "CAUGHT!";
            _catchTime = Time.time;
        }

        void Update()
        {
            // Catch card fades on its own after the minigame has finished
            float age = Time.time - _catchTime;
            if (age > 2.6f)
            {
                _catchGroup.alpha = Mathf.MoveTowards(_catchGroup.alpha, 0f, Time.deltaTime * 2.5f);
                return;
            }

            _catchGroup.alpha = age < 0.2f ? age / 0.2f : Mathf.Clamp01(1f - (age - 2.0f) / 0.6f);
            _catchCard.anchoredPosition = new Vector2(0f, 40f + Mathf.Min(age, 1f) * 18f);
        }

        // ==============================
        // UI BUILD (from GameHUD.BuildFishingOverlay / BuildCatchCard)
        // ==============================

        void BuildUi()
        {
            _canvas = ZoUIKit.CreateCanvas("TensionMinigameCanvas", 50);
            _canvas.transform.SetParent(transform, false);
            Transform root = _canvas.transform;

            // bite indicator
            var bite = ZoUIKit.Round(ZoUIKit.Box(root, "BiteRing", new Color(1f, 0.80f, 0.33f, 0.95f)), 48f);
            ZoUIKit.DropShadow(bite, 5f, 0.5f);
            _biteRingImage = bite;
            _biteRing = bite.rectTransform;
            ZoUIKit.Place(_biteRing, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(96f, 96f));
            _biteRing.pivot = new Vector2(0.5f, 0.5f);
            _biteText = ZoUIKit.Label(_biteRing, "BiteText", "!", 60, new Color(0.22f, 0.12f, 0.05f), TextAnchor.MiddleCenter, FontStyle.Bold);
            ZoUIKit.Stretch(_biteText.rectTransform);
            var strike = ZoUIKit.Label(_biteRing, "StrikeHint", "[SPACE] STRIKE", 18, ZoUIKit.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            strike.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            strike.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            strike.rectTransform.pivot = new Vector2(0.5f, 1f);
            strike.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            strike.rectTransform.sizeDelta = new Vector2(260f, 24f);
            _biteRing.gameObject.SetActive(false);

            // reeling card
            _reelCard = ZoUIKit.PanelBox(root, "ReelCard", ZoUIKit.PanelLo, ZoUIKit.Accent);
            ZoUIKit.Place(_reelCard, new Vector2(0.5f, 0f), new Vector2(0f, 160f), new Vector2(560f, 146f));
            _reelCard.pivot = new Vector2(0.5f, 0f);

            _reelSpecies = ZoUIKit.Label(_reelCard, "Species", "", 19, ZoUIKit.Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            ZoUIKit.Place(_reelSpecies.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(540f, 24f));

            var progLabel = ZoUIKit.Label(_reelCard, "ProgLabel", "LANDED", 11, ZoUIKit.Muted);
            ZoUIKit.Place(progLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -40f), new Vector2(120f, 14f));
            _progressFill = ZoUIKit.Bar(_reelCard, "Progress", ZoUIKit.Track, ZoUIKit.Good, out var pr);
            ZoUIKit.Place(pr, new Vector2(0f, 1f), new Vector2(20f, -56f), new Vector2(520f, 14f));

            var tenLabel = ZoUIKit.Label(_reelCard, "TenLabel", "LINE TENSION", 11, ZoUIKit.Muted);
            ZoUIKit.Place(tenLabel.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -78f), new Vector2(160f, 14f));

            var tensionBg = ZoUIKit.Round(ZoUIKit.Box(_reelCard, "TensionBg", ZoUIKit.Track), 6f);
            RectTransform tensionRoot = tensionBg.rectTransform;
            ZoUIKit.Place(tensionRoot, new Vector2(0f, 1f), new Vector2(20f, -94f), new Vector2(520f, 22f));

            _tensionBand = ZoUIKit.Box(tensionRoot, "Band", new Color(0.30f, 0.85f, 0.45f, 0.42f));
            _tensionBand.rectTransform.anchorMin = new Vector2(0.2f, 0f);
            _tensionBand.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _tensionBand.rectTransform.offsetMin = new Vector2(0f, 2f);
            _tensionBand.rectTransform.offsetMax = new Vector2(0f, -2f);

            _tensionFill = ZoUIKit.Box(tensionRoot, "Fill", new Color(0.36f, 0.76f, 0.80f, 0.60f));
            _tensionFill.rectTransform.anchorMin = new Vector2(0f, 0f);
            _tensionFill.rectTransform.anchorMax = new Vector2(0.3f, 1f);
            _tensionFill.rectTransform.offsetMin = new Vector2(2f, 3f);
            _tensionFill.rectTransform.offsetMax = new Vector2(0f, -3f);

            _tensionNeedle = ZoUIKit.Box(tensionRoot, "Needle", Color.white);
            _tensionNeedle.rectTransform.anchorMin = new Vector2(0.3f, 0f);
            _tensionNeedle.rectTransform.anchorMax = new Vector2(0.3f, 1f);
            _tensionNeedle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _tensionNeedle.rectTransform.sizeDelta = new Vector2(5f, 0f);
            _tensionNeedle.rectTransform.anchoredPosition = Vector2.zero;

            _stressFill = ZoUIKit.Bar(_reelCard, "Stress", ZoUIKit.Track, ZoUIKit.Bad, out var sr);
            ZoUIKit.Place(sr, new Vector2(0f, 1f), new Vector2(20f, -122f), new Vector2(520f, 7f));

            _reelHint = ZoUIKit.Label(_reelCard, "ReelHint", "HOLD [SPACE] to reel  -  keep the needle inside the green", 13, ZoUIKit.Muted, TextAnchor.UpperCenter);
            ZoUIKit.Place(_reelHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(540f, 18f));

            _reelCard.gameObject.SetActive(false);

            // catch card
            _catchCard = ZoUIKit.PanelBox(root, "CatchCard", new Color(0.15f, 0.17f, 0.08f, 0.95f), ZoUIKit.Good);
            ZoUIKit.Place(_catchCard, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(360f, 108f));
            _catchCard.pivot = new Vector2(0.5f, 0.5f);
            _catchGroup = _catchCard.gameObject.AddComponent<CanvasGroup>();
            _catchGroup.alpha = 0f;

            _catchName = ZoUIKit.Label(_catchCard, "Name", "", 24, ZoUIKit.Ink, TextAnchor.UpperCenter, FontStyle.Bold);
            ZoUIKit.Place(_catchName.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(340f, 28f));

            _catchStats = ZoUIKit.Label(_catchCard, "Stats", "", 14, ZoUIKit.Muted, TextAnchor.UpperCenter);
            ZoUIKit.Place(_catchStats.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(340f, 20f));

            _catchValue = ZoUIKit.Label(_catchCard, "Value", "", 22, ZoUIKit.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            ZoUIKit.Place(_catchValue.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(340f, 26f));
        }
    }

    /// <summary>
    /// Theme + widget helpers ported from the CODE demo's UIKit (only the parts the fishing overlay uses):
    /// walnut-wood panels, parchment text, rounded 9-sliced shapes, filled bars.
    /// </summary>
    public static class ZoUIKit
    {
        public static readonly Color Panel   = new Color(0.150f, 0.095f, 0.062f, 0.92f);
        public static readonly Color PanelLo = new Color(0.190f, 0.122f, 0.080f, 0.94f);
        public static readonly Color Accent  = new Color(0.960f, 0.580f, 0.260f, 1f);
        public static readonly Color Ink     = new Color(0.980f, 0.935f, 0.840f, 1f);
        public static readonly Color Muted   = new Color(0.800f, 0.690f, 0.550f, 1f);
        public static readonly Color Gold    = new Color(1.000f, 0.800f, 0.330f, 1f);
        public static readonly Color Good    = new Color(0.580f, 0.830f, 0.400f, 1f);
        public static readonly Color Bad     = new Color(0.930f, 0.400f, 0.300f, 1f);
        public static readonly Color Warn    = new Color(1.000f, 0.690f, 0.300f, 1f);
        public static readonly Color Track   = new Color(0.060f, 0.035f, 0.020f, 0.62f);
        public static readonly Color Rim     = new Color(0.640f, 0.450f, 0.270f, 0.85f);

        static Font _font;
        public static Font TheFont
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Helvetica", 16);
                return _font;
            }
        }

        static Sprite _white;
        public static Sprite White
        {
            get
            {
                if (_white != null) return _white;
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px);
                tex.Apply();
                tex.wrapMode = TextureWrapMode.Clamp;
                _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                return _white;
            }
        }

        const int RoundTex = 64;
        const float RoundRadius = 24f;
        const float RingWidth = 4f;

        static Sprite _round, _ring;
        public static Sprite RoundSprite => _round != null ? _round : (_round = MakeRounded(0f));
        public static Sprite RingSprite => _ring != null ? _ring : (_ring = MakeRounded(RingWidth));

        static Sprite MakeRounded(float ring)
        {
            int n = RoundTex;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[n * n];
            float half = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (half - RoundRadius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - RoundRadius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - RoundRadius;
                float a = Mathf.Clamp01(0.5f - d);
                if (ring > 0f) a *= Mathf.Clamp01(d + ring + 0.5f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            float b = RoundRadius + 1f;
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        public static Canvas CreateCanvas(string name, int sortOrder)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static Image Round(Image img, float radius = 10f)
        {
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = RoundRadius / Mathf.Max(1f, radius);
            return img;
        }

        public static void DropShadow(Graphic g, float dist = 4f, float alpha = 0.45f)
        {
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, alpha);
            sh.effectDistance = new Vector2(0f, -dist);
        }

        static Image Decor(Transform parent, string name, Sprite sprite, Color color, float radius)
        {
            var img = Box(parent, name, color);
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = RoundRadius / Mathf.Max(1f, radius);
            img.raycastTarget = false;
            Stretch(img.rectTransform);
            return img;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Box(Transform parent, string name, Color color)
        {
            var img = Rect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = White;
            img.color = color;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Rounded wooden panel with a light rim and an accent strip along the top.</summary>
        public static RectTransform PanelBox(Transform parent, string name, Color? bg = null, Color? accent = null)
        {
            var img = Round(Box(parent, name, bg ?? Panel), 12f);
            DropShadow(img, 5f, 0.40f);
            var acc = accent ?? Accent;
            Decor(img.transform, "Rim", RingSprite, Color.Lerp(Rim, acc, 0.30f), 12f);

            var strip = Decor(img.transform, "Accent", RoundSprite, acc, 2f);
            Anchor(strip.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -5f), new Vector2(-14f, -2f));
            return img.rectTransform;
        }

        public static Text Label(Transform parent, string name, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = TheFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (style == FontStyle.Bold && size >= 17)
            {
                var sh = rt.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0.05f, 0.02f, 0f, 0.65f);
                sh.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return t;
        }

        public static Image Bar(Transform parent, string name, Color bg, Color fill, out RectTransform root)
        {
            var b = Round(Box(parent, name, bg), 5f);
            root = b.rectTransform;
            var clip = Round(Box(b.transform, "Clip", Color.white), 4f);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            Anchor(clip.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var f = Box(clip.transform, "Fill", fill);
            Stretch(f.rectTransform);
            f.type = Image.Type.Filled;
            f.fillMethod = Image.FillMethod.Horizontal;
            f.fillOrigin = 0;
            f.fillAmount = 1f;
            return f;
        }

        public static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        public static void Stretch(RectTransform rt, float padX = 0f, float padY = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }
    }
}
