using UnityEngine;
using UnityEngine.UI;

namespace BigFish
{
    /// <summary>
    /// Hold-to-rise catch slider.
    /// A vertical bar has a green zone in the middle. HOLD Space to push the slider up,
    /// LET GO and it falls. Keep the slider inside the green zone to fill the catch meter.
    /// Full meter = caught. Empty meter = the fish gets away.
    ///
    /// UI is optional: leave the UI slots empty and a simple bar is built at runtime.
    /// To restyle it, build your own Canvas and assign the slots
    /// (Zone and Marker must be children of Bar, Progress Fill a child of Progress Bar).
    /// </summary>
    public class ZoSliderCatchMinigame : ZoCatchMinigame
    {
        [Header("Green Zone (0 = bottom of bar, 1 = top)")]
        [Range(0f, 1f)] [SerializeField] float zoneCenter = 0.5f;
        [Range(0.05f, 1f)] [SerializeField] float zoneSize = 0.25f;

        [Header("Slider Movement (bar heights per second)")]
        [Tooltip("Upward push while Space is held.")]
        [SerializeField] float liftAcceleration = 2.6f;
        [Tooltip("Downward pull when Space is released.")]
        [SerializeField] float gravity = 2.2f;
        [SerializeField] float maxSpeed = 1.4f;
        [Tooltip("How much speed is kept when the slider hits the top or bottom (0 = dead stop).")]
        [Range(0f, 1f)] [SerializeField] float edgeBounce = 0.3f;

        [Header("Fish Fight")]
        [Tooltip("Random shove from the fish. 0 = fish doesn't fight.")]
        [SerializeField] float fishPullStrength = 0.8f;
        [Tooltip("Seconds between the fish changing direction.")]
        [SerializeField] Vector2 fishPullInterval = new Vector2(0.6f, 1.4f);

        [Header("Catch Meter")]
        [Range(0f, 1f)] [SerializeField] float startProgress = 0.3f;
        [Tooltip("Meter gained per second while in the green.")]
        [SerializeField] float fillRate = 0.25f;
        [Tooltip("Meter lost per second while outside the green.")]
        [SerializeField] float drainRate = 0.18f;

        [Header("UI (optional - auto-built if empty)")]
        [SerializeField] GameObject root;
        [SerializeField] RectTransform bar;
        [SerializeField] RectTransform zone;
        [SerializeField] RectTransform marker;
        [SerializeField] RectTransform progressBar;
        [SerializeField] RectTransform progressFill;
        [SerializeField] Text label;

        [Header("Default UI Colors")]
        [SerializeField] Color barColor = new Color(0.12f, 0.18f, 0.28f, 0.95f);
        [SerializeField] Color zoneColor = new Color(0.25f, 0.85f, 0.35f, 0.9f);
        [SerializeField] Color markerColor = Color.white;
        [SerializeField] Color markerInZoneColor = new Color(0.6f, 1f, 0.6f, 1f);
        [SerializeField] Color progressColor = new Color(1f, 0.8f, 0.2f, 1f);

        float _pos;
        float _vel;
        float _progress;
        float _fishPull;
        float _fishTimer;
        Graphic _markerGraphic;

        public float SliderPosition => _pos;
        public float Progress => _progress;
        public bool InZone => Mathf.Abs(_pos - zoneCenter) <= zoneSize * 0.5f;

        void Awake()
        {
            if (root == null || bar == null || zone == null || marker == null)
                BuildDefaultUi();

            _markerGraphic = marker.GetComponent<Graphic>();
            root.SetActive(false);
        }

        protected override void OnBegin(CatchInfo fish)
        {
            _pos = zoneCenter;
            _vel = 0f;
            _progress = startProgress;
            _fishPull = 0f;
            _fishTimer = 0f;

            root.SetActive(true);
            Refresh();
        }

        protected override void OnEnd()
        {
            if (root != null)
                root.SetActive(false);
        }

        public override void Tick(float deltaTime, bool actionPressed, bool actionHeld)
        {
            if (!IsRunning)
                return;

            // Fish randomly shoves the slider up or down
            _fishTimer -= deltaTime;
            if (_fishTimer <= 0f)
            {
                _fishTimer = Random.Range(fishPullInterval.x, fishPullInterval.y);
                _fishPull = Random.Range(-1f, 1f) * fishPullStrength;
            }

            // Hold = push up, release = fall
            float accel = (actionHeld ? liftAcceleration : -gravity) + _fishPull;
            _vel = Mathf.Clamp(_vel + accel * deltaTime, -maxSpeed, maxSpeed);
            _pos += _vel * deltaTime;

            if (_pos < 0f) { _pos = 0f; _vel = -_vel * edgeBounce; }
            if (_pos > 1f) { _pos = 1f; _vel = -_vel * edgeBounce; }

            _progress += (InZone ? fillRate : -drainRate) * deltaTime;

            if (_progress >= 1f)
            {
                _progress = 1f;
                Refresh();
                Finish(true);
                return;
            }

            if (_progress <= 0f)
            {
                _progress = 0f;
                Refresh();
                Finish(false);
                return;
            }

            Refresh();
        }

        void Refresh()
        {
            float half = zoneSize * 0.5f;
            zone.anchorMin = new Vector2(0f, Mathf.Clamp01(zoneCenter - half));
            zone.anchorMax = new Vector2(1f, Mathf.Clamp01(zoneCenter + half));
            zone.offsetMin = Vector2.zero;
            zone.offsetMax = Vector2.zero;

            marker.anchorMin = new Vector2(0.5f, _pos);
            marker.anchorMax = new Vector2(0.5f, _pos);
            marker.anchoredPosition = Vector2.zero;

            if (_markerGraphic != null)
                _markerGraphic.color = InZone ? markerInZoneColor : markerColor;

            if (progressFill != null)
            {
                progressFill.anchorMin = Vector2.zero;
                progressFill.anchorMax = new Vector2(1f, _progress);
                progressFill.offsetMin = Vector2.zero;
                progressFill.offsetMax = Vector2.zero;
            }

            if (label != null)
                label.text = "FISH ON!\nHold SPACE to rise\nKeep it in the green";
        }

        // ==============================
        // DEFAULT UI
        // ==============================

        void BuildDefaultUi()
        {
            var canvasGo = new GameObject("CatchMinigameCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            root = canvasGo;

            // Panel on the right-middle of the screen
            RectTransform panel = MakeImage("Panel", canvasGo.transform, new Color(0f, 0f, 0f, 0.55f));
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.sizeDelta = new Vector2(220f, 560f);
            panel.anchoredPosition = new Vector2(-80f, 0f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(panel, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.sizeDelta = new Vector2(0f, 80f);
            labelRect.anchoredPosition = new Vector2(0f, -8f);
            label = labelGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 18;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;

            // Tall slider bar
            bar = MakeImage("Bar", panel, barColor);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(60f, 440f);
            bar.anchoredPosition = new Vector2(-25f, 20f);

            zone = MakeImage("Zone", bar, zoneColor);

            marker = MakeImage("Marker", bar, markerColor);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(80f, 14f);

            // Catch meter next to it
            progressBar = MakeImage("ProgressBar", panel, new Color(0.08f, 0.08f, 0.08f, 0.9f));
            progressBar.anchorMin = progressBar.anchorMax = new Vector2(0.5f, 0f);
            progressBar.pivot = new Vector2(0.5f, 0f);
            progressBar.sizeDelta = new Vector2(22f, 440f);
            progressBar.anchoredPosition = new Vector2(50f, 20f);

            progressFill = MakeImage("ProgressFill", progressBar, progressColor);
        }

        static RectTransform MakeImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }
    }
}
