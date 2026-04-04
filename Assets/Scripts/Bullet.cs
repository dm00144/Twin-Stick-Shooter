using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public static class ScoreTracker
{
    public static int CurrentScore { get; private set; }

    public static void Add(int amount)
    {
        CurrentScore += Mathf.Max(0, amount);
    }

    public static void Reset()
    {
        CurrentScore = 0;
    }
}

public static class GameFlowManager
{
    public static bool IsGameplayActive { get; private set; }
    public static bool IsGameOver { get; private set; }
    public static bool IsVictory { get; private set; }

    public static event System.Action StateChanged;

    public static void ShowStartScreen()
    {
        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = false;
        StateChanged?.Invoke();
    }

    public static void StartGameplay()
    {
        ScoreTracker.Reset();
        IsGameplayActive = true;
        IsGameOver = false;
        IsVictory = false;
        StateChanged?.Invoke();
    }

    public static void TriggerGameOver()
    {
        if (IsGameOver)
            return;

        IsGameplayActive = false;
        IsGameOver = true;
        IsVictory = false;
        StateChanged?.Invoke();
    }

    public static void TriggerVictory()
    {
        if (IsVictory)
            return;

        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = true;
        StateChanged?.Invoke();
    }
}

public class GameHUD : MonoBehaviour
{
    private static GameHUD instance;

    private PlayerMovement player;
    private Text scoreText;
    private Text hullText;
    private RectTransform healthFill;
    private float healthFillWidth = 300f;
    private GameObject startPanel;
    private GameObject gameOverPanel;
    private Text centerTitleText;
    private Text centerBodyText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        ScoreTracker.Reset();
        GameFlowManager.ShowStartScreen();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject hudObject = new GameObject("GameHUD");
        instance = hudObject.AddComponent<GameHUD>();
        DontDestroyOnLoad(hudObject);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        CreateCanvas();
        GameFlowManager.StateChanged += RefreshGameStateUI;
        RefreshGameStateUI();
    }

    private void OnDestroy()
    {
        GameFlowManager.StateChanged -= RefreshGameStateUI;
    }

    private void Update()
    {
        HandleStateInput();

        if (player == null)
            player = FindFirstObjectByType<PlayerMovement>();

        if (scoreText == null || hullText == null || healthFill == null)
            return;

        scoreText.text = $"Score: {ScoreTracker.CurrentScore}";

        if (player == null)
        {
            hullText.text = "Hull: --";
            healthFill.sizeDelta = new Vector2(0f, healthFill.sizeDelta.y);
            return;
        }

        hullText.text = $"Hull: {player.CurrentHealth:0.0}/{player.MaxHealth:0.0}";
        healthFill.sizeDelta = new Vector2(healthFillWidth * player.HealthNormalized, healthFill.sizeDelta.y);
    }

    private void CreateCanvas()
    {
        if (transform.Find("HUDCanvas") != null)
            return;

        GameObject canvasObject = new GameObject("HUDCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.AddComponent<GraphicRaycaster>();

        healthFillWidth = 160f;
        GameObject panel = CreatePanel("Panel", canvasObject.transform, new Vector2(12f, -12f), new Vector2(190f, 70f), new Color(0f, 0f, 0f, 0.35f));
        scoreText = CreateText("ScoreText", panel.transform, new Vector2(10f, -8f), new Vector2(160f, 18f), 14, TextAnchor.UpperLeft);
        hullText = CreateText("HullText", panel.transform, new Vector2(10f, -28f), new Vector2(160f, 18f), 14, TextAnchor.UpperLeft);

        GameObject barBackground = CreatePanel("HealthBarBg", panel.transform, new Vector2(10f, -50f), new Vector2(healthFillWidth, 10f), new Color(0.18f, 0.1f, 0.1f, 0.95f));
        GameObject fill = CreatePanel("HealthBarFill", barBackground.transform, Vector2.zero, new Vector2(healthFillWidth, 10f), new Color(0.3f, 0.95f, 0.45f, 1f));
        healthFill = fill.GetComponent<RectTransform>();
        healthFill.anchorMin = new Vector2(0f, 0.5f);
        healthFill.anchorMax = new Vector2(0f, 0.5f);
        healthFill.pivot = new Vector2(0f, 0.5f);
        healthFill.anchoredPosition = Vector2.zero;

        startPanel = CreateCenterPanel("StartPanel", canvasObject.transform, new Vector2(420f, 170f), new Color(0f, 0f, 0f, 0.55f));
        centerTitleText = CreateCenteredText("StartTitle", startPanel.transform, new Vector2(30f, -26f), new Vector2(360f, 40f), 28, TextAnchor.MiddleCenter);
        centerBodyText = CreateCenteredText("StartBody", startPanel.transform, new Vector2(30f, -78f), new Vector2(360f, 60f), 18, TextAnchor.MiddleCenter);

        gameOverPanel = CreateCenterPanel("GameOverPanel", canvasObject.transform, new Vector2(420f, 170f), new Color(0f, 0f, 0f, 0.6f));
        gameOverPanel.SetActive(false);
    }

    private static GameObject CreatePanel(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image image = panel.AddComponent<Image>();
        image.color = color;

        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        return panel;
    }

    private static Text CreateText(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = Color.white;

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        return text;
    }

    private static Text CreateCenteredText(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = Color.white;

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        return text;
    }

    private static GameObject CreateCenterPanel(string name, Transform parent, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image image = panel.AddComponent<Image>();
        image.color = color;

        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = size;

        return panel;
    }

    private void HandleStateInput()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            if (!GameFlowManager.IsGameplayActive && !GameFlowManager.IsGameOver && !GameFlowManager.IsVictory)
            {
                GameFlowManager.StartGameplay();
                return;
            }

            if (GameFlowManager.IsGameOver || GameFlowManager.IsVictory)
                RestartScene();
        }
    }

    private void RefreshGameStateUI()
    {
        if (startPanel == null || gameOverPanel == null || centerTitleText == null || centerBodyText == null)
            return;

        startPanel.SetActive(!GameFlowManager.IsGameplayActive && !GameFlowManager.IsGameOver && !GameFlowManager.IsVictory);
        gameOverPanel.SetActive(GameFlowManager.IsGameOver || GameFlowManager.IsVictory);

        if (!GameFlowManager.IsGameplayActive && !GameFlowManager.IsGameOver && !GameFlowManager.IsVictory)
        {
            centerTitleText.text = "Twin Stick Shooter";
            centerBodyText.text = "Press Space or Enter to start";
        }

        if (GameFlowManager.IsGameOver)
        {
            centerTitleText.text = "Game Over";
            centerBodyText.text = $"Final Score: {ScoreTracker.CurrentScore}\nPress Space or Enter to restart";
            startPanel.SetActive(true);
        }

        if (GameFlowManager.IsVictory)
        {
            centerTitleText.text = "Boss Destroyed";
            centerBodyText.text = $"Final Score: {ScoreTracker.CurrentScore}\nPress Space or Enter to restart";
            startPanel.SetActive(true);
        }
    }

    private static void RestartScene()
    {
        ScoreTracker.Reset();
        GameFlowManager.ShowStartScreen();
        Scene activeScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(activeScene.name);
    }
}
