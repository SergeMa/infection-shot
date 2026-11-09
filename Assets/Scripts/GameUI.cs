using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Simple
{
    /// <summary>
    /// Minimal UI, built in code at startup:
    /// a size bar at the top (player size, with a red marker at the lose threshold)
    /// and a win / lose panel with a restart button.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        [SerializeField] private GameController gameController;
        [SerializeField] private Player player;

        [Header("Look")]
        [SerializeField] private Color barBackground = new Color(0f, 0f, 0f, 0.45f);
        [SerializeField] private Color barSafe = new Color(0.35f, 0.85f, 0.4f);
        [SerializeField] private Color barDanger = new Color(1f, 0.6f, 0.2f);
        [SerializeField] private Color thresholdColor = new Color(0.95f, 0.15f, 0.15f);

        [Tooltip("The bar turns to the danger colour when size is closer than this to the lose threshold.")]
        [SerializeField, Min(0f)] private float dangerMargin = 0.3f;

        private Font _font;
        private RectTransform _fill;
        private Image _fillImage;
        private RectTransform _thresholdMarker;
        private Text _sizeLabel;
        private GameObject _resultPanel;
        private Text _resultTitle;
        private Text _resultReason;

        private float _maxSize; // the bar's full length = the biggest size the player has had

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = BuildCanvas();
            BuildSizeBar(canvas);
            BuildResultPanel(canvas);
            EnsureEventSystem();
        }

        private void OnEnable()
        {
            if (player != null) player.SizeChanged += HandleSizeChanged;
            if (gameController != null)
            {
                gameController.Won += HandleWon;
                gameController.Lost += HandleLost;
            }
        }

        private void OnDisable()
        {
            if (player != null) player.SizeChanged -= HandleSizeChanged;
            if (gameController != null)
            {
                gameController.Won -= HandleWon;
                gameController.Lost -= HandleLost;
            }
        }

        private void Start()
        {
            if (player != null) HandleSizeChanged(player.Size);
        }

        // ---- Events ------------------------------------------------------------------------

        private void HandleSizeChanged(float size)
        {
            _maxSize = Mathf.Max(_maxSize, size);
            if (_maxSize <= 0f) return;

            float threshold = gameController != null ? gameController.MinPlayerSize : 0f;

            _fill.anchorMax = new Vector2(Mathf.Clamp01(size / _maxSize), 1f);
            _fillImage.color = size < threshold + dangerMargin ? barDanger : barSafe;

            float markerX = Mathf.Clamp01(threshold / _maxSize);
            _thresholdMarker.anchorMin = new Vector2(markerX, 0f);
            _thresholdMarker.anchorMax = new Vector2(markerX, 1f);

            _sizeLabel.text = FormatSize(size);
        }

        /// <summary>
        /// "1.84"-style label. Size changes every frame while charging, so each distinct value is formatted
        /// once and cached instead of allocating a new string per frame.
        /// </summary>
        private static string FormatSize(float size)
        {
            int hundredths = Mathf.Clamp(Mathf.RoundToInt(size * 100f), 0, s_sizeLabels.Length - 1);
            return s_sizeLabels[hundredths] ??= (hundredths / 100f).ToString("0.00");
        }

        private static readonly string[] s_sizeLabels = new string[2001]; // 0.00 .. 20.00

        private void HandleWon() => ShowResult("Перемога!", string.Empty, barSafe);

        private void HandleLost() => ShowResult("Поразка", gameController.LoseReason, thresholdColor);

        private void ShowResult(string title, string reason, Color color)
        {
            _resultTitle.text = title;
            _resultTitle.color = color;
            _resultReason.text = reason;
            _resultPanel.SetActive(true);
        }

        private static void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---- Building ----------------------------------------------------------------------

        private RectTransform BuildCanvas()
        {
            var go = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            return (RectTransform)go.transform;
        }

        private void BuildSizeBar(RectTransform canvas)
        {
            // Background strip at the top of the screen.
            var bar = CreateImage("SizeBar", canvas, barBackground);
            bar.anchorMin = new Vector2(0.1f, 1f);
            bar.anchorMax = new Vector2(0.9f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -120f);
            bar.sizeDelta = new Vector2(0f, 48f);

            // Fill: its right anchor follows size / maxSize.
            _fill = CreateImage("Fill", bar, barSafe);
            Stretch(_fill);
            _fillImage = _fill.GetComponent<Image>();

            // Lose threshold marker: a thin vertical line that sticks out above and below the bar.
            _thresholdMarker = CreateImage("LoseThreshold", bar, thresholdColor);
            _thresholdMarker.pivot = new Vector2(0.5f, 0.5f);
            _thresholdMarker.sizeDelta = new Vector2(8f, 24f);
            _thresholdMarker.anchoredPosition = Vector2.zero;

            var title = CreateText("Title", bar, "Розмір", 34, TextAnchor.LowerLeft, Color.white);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 0f);
            title.rectTransform.sizeDelta = new Vector2(0f, 44f);
            title.rectTransform.anchoredPosition = new Vector2(0f, 6f);

            _sizeLabel = CreateText("Value", bar, "", 32, TextAnchor.MiddleCenter, Color.white);
            Stretch(_sizeLabel.rectTransform);
        }

        private void BuildResultPanel(RectTransform canvas)
        {
            var dim = CreateImage("ResultPanel", canvas, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim);
            _resultPanel = dim.gameObject;

            var box = CreateImage("Box", dim, new Color(1f, 1f, 1f, 0.95f));
            box.sizeDelta = new Vector2(760f, 500f);

            _resultTitle = CreateText("Title", box, "", 90, TextAnchor.MiddleCenter, Color.black);
            _resultTitle.fontStyle = FontStyle.Bold;
            _resultTitle.rectTransform.anchorMin = new Vector2(0f, 0.62f);
            _resultTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _resultTitle.rectTransform.sizeDelta = Vector2.zero;

            _resultReason = CreateText("Reason", box, "", 38, TextAnchor.MiddleCenter, new Color(0.25f, 0.25f, 0.25f));
            _resultReason.rectTransform.anchorMin = new Vector2(0.05f, 0.4f);
            _resultReason.rectTransform.anchorMax = new Vector2(0.95f, 0.62f);
            _resultReason.rectTransform.sizeDelta = Vector2.zero;

            var button = CreateImage("RestartButton", box, new Color(0.25f, 0.5f, 0.95f));
            button.anchorMin = button.anchorMax = new Vector2(0.5f, 0.2f);
            button.sizeDelta = new Vector2(420f, 120f);
            button.gameObject.AddComponent<Button>().onClick.AddListener(Restart);

            var label = CreateText("Label", button, "Ще раз", 54, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);

            _resultPanel.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Text CreateText(string name, Transform parent, string value, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
