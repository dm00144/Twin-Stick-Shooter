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
    public static bool IsUpgradeChoiceActive { get; private set; }
    public static bool IsStoryBlurbActive { get; private set; }
    public static bool IsPostFinalStoryBlurb { get; private set; }

    public static event System.Action StateChanged;

    public static void ShowStartScreen()
    {
        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = false;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = false;
        IsPostFinalStoryBlurb = false;
        StateChanged?.Invoke();
    }

    public static void StartGameplay()
    {
        // Starting the run immediately routes through the stage blurb, because pilots deserve a briefing.
        ScoreTracker.Reset();
        StageProgression.StartNewRun();
        IsGameplayActive = true;
        IsGameOver = false;
        IsVictory = false;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = false;
        IsPostFinalStoryBlurb = false;
        StageSoftReset.ResetForNextStage();
        ShowStageStoryBlurb();
    }

    public static void ShowStageStoryBlurb()
    {
        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = false;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = true;
        IsPostFinalStoryBlurb = false;
        StateChanged?.Invoke();
    }

    public static void TriggerGameOver()
    {
        if (IsGameOver)
            return;

        IsGameplayActive = false;
        IsGameOver = true;
        IsVictory = false;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = false;
        IsPostFinalStoryBlurb = false;
        StateChanged?.Invoke();
    }

    public static void TriggerPlayerDeath()
    {
        if (StageProgression.TrySpendLife())
        {
            // Still have lives, so skip the drama and put the player back in the air.
            IsGameplayActive = true;
            IsGameOver = false;
            IsVictory = false;
            IsUpgradeChoiceActive = false;
            IsStoryBlurbActive = false;
            IsPostFinalStoryBlurb = false;
            StateChanged?.Invoke();
            return;
        }

        TriggerGameOver();
    }

    public static void TriggerVictory()
    {
        if (IsVictory)
            return;

        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = true;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = false;
        IsPostFinalStoryBlurb = false;
        StateChanged?.Invoke();
    }

    public static void TriggerFinalStoryBlurb()
    {
        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = false;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = true;
        IsPostFinalStoryBlurb = true;
        StateChanged?.Invoke();
    }

    public static void TriggerUpgradeChoice()
    {
        if (IsUpgradeChoiceActive)
            return;

        IsGameplayActive = false;
        IsGameOver = false;
        IsVictory = false;
        IsUpgradeChoiceActive = true;
        IsStoryBlurbActive = false;
        IsPostFinalStoryBlurb = false;
        StateChanged?.Invoke();
    }

    public static void ResumeGameplayAfterUpgrade()
    {
        IsGameplayActive = true;
        IsGameOver = false;
        IsVictory = false;
        IsUpgradeChoiceActive = false;
        IsStoryBlurbActive = false;
        IsPostFinalStoryBlurb = false;
        StateChanged?.Invoke();
    }

    public static void AdvanceStoryBlurb()
    {
        if (!IsStoryBlurbActive)
            return;

        // Final blurb hands off to victory; normal blurbs hand off to active combat.
        if (IsPostFinalStoryBlurb)
        {
            TriggerVictory();
            return;
        }

        ResumeGameplayAfterUpgrade();
    }
}

public class GameHUD : MonoBehaviour
{
    private static GameHUD instance;
    private static Sprite hudPanelSprite;
    private static Sprite modalPanelSprite;

    private PlayerMovement player;
    private Text scoreText;
    private Text stageText;
    private Text livesText;
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
        StageProgression.ChoicesChanged += RefreshGameStateUI;
        UpgradeSystem.UpgradesChanged += RefreshGameStateUI;
        RefreshGameStateUI();
    }

    private void OnDestroy()
    {
        GameFlowManager.StateChanged -= RefreshGameStateUI;
        StageProgression.ChoicesChanged -= RefreshGameStateUI;
        UpgradeSystem.UpgradesChanged -= RefreshGameStateUI;
    }

    private void Update()
    {
        HandleStateInput();

        if (player == null)
            player = FindFirstObjectByType<PlayerMovement>();

        if (scoreText == null || hullText == null || healthFill == null)
            return;

        scoreText.text = $"Score: {ScoreTracker.CurrentScore}";
        if (stageText != null)
            stageText.text = $"Stage {StageProgression.CurrentStage}/{StageProgression.MaxStage}: Destroy Boss";
        if (livesText != null)
            livesText.text = $"Lives: {StageProgression.LivesRemaining}";

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

        // HUD is runtime-built so the scene stays light and every scene gets the same cockpit overlay.
        GameObject canvasObject = new GameObject("HUDCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.AddComponent<GraphicRaycaster>();

        healthFillWidth = 70f;
        GameObject panel = CreatePanel("Panel", canvasObject.transform, new Vector2(8f, -8f), new Vector2(140f, 74f), Color.white);
        scoreText = CreateText("ScoreText", panel.transform, new Vector2(13f, -12f), new Vector2(112f, 10f), 7, TextAnchor.UpperLeft);
        stageText = CreateText("StageText", panel.transform, new Vector2(13f, -23f), new Vector2(120f, 10f), 7, TextAnchor.UpperLeft);
        livesText = CreateText("LivesText", panel.transform, new Vector2(13f, -34f), new Vector2(96f, 10f), 7, TextAnchor.UpperLeft);
        hullText = CreateText("HullText", panel.transform, new Vector2(13f, -45f), new Vector2(96f, 10f), 7, TextAnchor.UpperLeft);

        GameObject barBackground = CreateStatusBar("HealthBarBg", panel.transform, new Vector2(15f, -61f), new Vector2(healthFillWidth, 4f), new Color(0.04f, 0.07f, 0.1f, 0.82f));
        GameObject fill = CreateStatusBar("HealthBarFill", barBackground.transform, Vector2.zero, new Vector2(healthFillWidth, 4f), new Color(0f, 0.85f, 0.95f, 0.95f));
        healthFill = fill.GetComponent<RectTransform>();
        healthFill.anchorMin = new Vector2(0f, 0.5f);
        healthFill.anchorMax = new Vector2(0f, 0.5f);
        healthFill.pivot = new Vector2(0f, 0.5f);
        healthFill.anchoredPosition = Vector2.zero;

        // Fixed-size pixel panels won out over 9-slice. Sometimes the simple road is the sane road.
        startPanel = CreateCenterPanel("StartPanel", canvasObject.transform, new Vector2(560f, 290f), Color.white);
        centerTitleText = CreateCenteredText("StartTitle", startPanel.transform, new Vector2(38f, -32f), new Vector2(484f, 40f), 26, TextAnchor.MiddleCenter);
        centerBodyText = CreateCenteredText("StartBody", startPanel.transform, new Vector2(42f, -84f), new Vector2(476f, 174f), 14, TextAnchor.MiddleCenter);

        gameOverPanel = CreateCenterPanel("GameOverPanel", canvasObject.transform, new Vector2(560f, 290f), Color.white);
        gameOverPanel.SetActive(false);
    }

    private static GameObject CreatePanel(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);

        Image image = panel.AddComponent<Image>();
        image.color = color;
        image.sprite = GetFixedPanelSprite("UI/PixelMilitaryHudPanel", ref hudPanelSprite, "PixelMilitaryHudPanel");
        image.type = Image.Type.Simple;

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
        text.color = new Color(0.78f, 0.96f, 1f, 1f);
        text.raycastTarget = false;

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
        text.color = new Color(0.78f, 0.96f, 1f, 1f);
        text.raycastTarget = false;

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
        image.sprite = GetFixedPanelSprite("UI/PixelMilitaryModalPanel", ref modalPanelSprite, "PixelMilitaryModalPanel");
        image.type = Image.Type.Simple;

        RectTransform rectTransform = panel.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = size;

        return panel;
    }

    private static GameObject CreateStatusBar(string name, Transform parent, Vector2 anchoredPosition, Vector2 size, Color color)
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

    private static Sprite GetFixedPanelSprite(string resourcePath, ref Sprite cachedSprite, string spriteName)
    {
        if (cachedSprite != null)
            return cachedSprite;

        // These are fixed panel PNGs in Resources/UI, not sliced sprites doing math gymnastics.
        Texture2D assetTexture = Resources.Load<Texture2D>(resourcePath);
        if (assetTexture != null)
        {
            assetTexture.filterMode = FilterMode.Point;
            assetTexture.wrapMode = TextureWrapMode.Clamp;
            cachedSprite = Sprite.Create(
                assetTexture,
                new Rect(0f, 0f, assetTexture.width, assetTexture.height),
                new Vector2(0.5f, 0.5f),
                1f);
            cachedSprite.name = spriteName;
            return cachedSprite;
        }

        cachedSprite = CreateFallbackPanelSprite(spriteName);
        return cachedSprite;
    }

    private static Sprite CreateFallbackPanelSprite(string spriteName)
    {
        // If the real art is missing, draw a basic emergency panel so the UI still exists.
        const int size = 64;
        const int border = 18;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color outline = new Color(0.01f, 0.015f, 0.025f, 1f);
        Color shadow = new Color(0.04f, 0.07f, 0.11f, 0.96f);
        Color plate = new Color(0.11f, 0.16f, 0.22f, 0.96f);
        Color fill = new Color(0.035f, 0.055f, 0.08f, 0.9f);
        Color steel = new Color(0.34f, 0.42f, 0.5f, 1f);
        Color cyan = new Color(0f, 0.86f, 1f, 1f);
        Color orange = new Color(1f, 0.45f, 0f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, clear);
        }

        DrawPanelLayer(texture, 0, outline);
        DrawPanelLayer(texture, 3, shadow);
        DrawPanelLayer(texture, 7, plate);
        DrawPanelLayer(texture, 13, fill);

        DrawRect(texture, 13, size - 10, 16, 3, steel);
        DrawRect(texture, size - 16, 13, 3, size - 26, outline);
        DrawRect(texture, 10, 13, 3, size - 26, outline);
        DrawRect(texture, 27, size - 13, 10, 3, cyan);
        DrawRect(texture, 27, 10, 10, 3, cyan);
        DrawRect(texture, size - 18, size - 21, 3, 3, orange);
        DrawRect(texture, size - 23, size - 16, 3, 3, orange);

        texture.Apply();
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            1f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        sprite.name = spriteName + "Fallback";
        return sprite;
    }

    private static void DrawPanelLayer(Texture2D texture, int inset, Color color)
    {
        int size = texture.width;
        int bevel = Mathf.Max(3, 9 - inset / 2);
        for (int y = inset; y < size - inset; y++)
        {
            for (int x = inset; x < size - inset; x++)
            {
                int left = x - inset;
                int right = size - inset - 1 - x;
                int bottom = y - inset;
                int top = size - inset - 1 - y;

                if (left + bottom < bevel || right + bottom < bevel || left + top < bevel || right + top < bevel)
                    continue;

                texture.SetPixel(x, y, color);
            }
        }
    }

    private static void DrawRect(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        for (int py = y; py < y + height; py++)
        {
            for (int px = x; px < x + width; px++)
            {
                if (px < 0 || py < 0 || px >= texture.width || py >= texture.height)
                    continue;

                texture.SetPixel(px, py, color);
            }
        }
    }

    private void HandleStateInput()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            // Story cards are Enter-only so Space can stay mentally attached to "shoot things."
            if (Input.GetKeyDown(KeyCode.Return) && GameFlowManager.IsStoryBlurbActive)
            {
                GameFlowManager.AdvanceStoryBlurb();
                return;
            }

            if (!GameFlowManager.IsGameplayActive && !GameFlowManager.IsGameOver && !GameFlowManager.IsVictory && !GameFlowManager.IsUpgradeChoiceActive && !GameFlowManager.IsStoryBlurbActive)
            {
                GameFlowManager.StartGameplay();
                return;
            }

            if (GameFlowManager.IsGameOver || GameFlowManager.IsVictory)
                RestartScene();
        }

        if (!GameFlowManager.IsUpgradeChoiceActive)
            return;

        // Upgrade selection is intentionally keyboard-simple for now: 1, 2, 3, done.
        if (Input.GetKeyDown(KeyCode.Alpha1))
            StageProgression.SelectUpgrade(0);
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            StageProgression.SelectUpgrade(1);
        else if (Input.GetKeyDown(KeyCode.Alpha3))
            StageProgression.SelectUpgrade(2);
    }

    private void RefreshGameStateUI()
    {
        if (startPanel == null || gameOverPanel == null || centerTitleText == null || centerBodyText == null)
            return;

        startPanel.SetActive(!GameFlowManager.IsGameplayActive);
        gameOverPanel.SetActive(GameFlowManager.IsGameOver || GameFlowManager.IsVictory);

        if (!GameFlowManager.IsGameplayActive && !GameFlowManager.IsGameOver && !GameFlowManager.IsVictory && !GameFlowManager.IsUpgradeChoiceActive && !GameFlowManager.IsStoryBlurbActive)
        {
            centerTitleText.text = "Twin Stick Shooter";
            centerBodyText.text = "Press Space or Enter to start";
        }

        if (GameFlowManager.IsStoryBlurbActive)
        {
            // Story text lives here until it earns its own data file.
            centerTitleText.text = GameFlowManager.IsPostFinalStoryBlurb
                ? "Earth Secured"
                : $"Stage {StageProgression.CurrentStage}";
            centerBodyText.text = GameFlowManager.IsPostFinalStoryBlurb
                ? $"{GetFinalStoryBlurb()}\n\nPress Enter to continue"
                : $"{GetStageStoryBlurb(StageProgression.CurrentStage)}\n\nPress Enter to continue";
            startPanel.SetActive(true);
        }

        if (GameFlowManager.IsUpgradeChoiceActive)
        {
            var choices = UpgradeSystem.CurrentChoices;
            centerTitleText.text = $"Stage {StageProgression.CurrentStage} Complete";
            centerBodyText.text =
                $"Choose an upgrade\n" +
                $"1. {UpgradeSystem.GetDisplayName(choices[0])}: {UpgradeSystem.GetDescription(choices[0])}\n" +
                $"2. {UpgradeSystem.GetDisplayName(choices[1])}: {UpgradeSystem.GetDescription(choices[1])}\n" +
                $"3. {UpgradeSystem.GetDisplayName(choices[2])}: {UpgradeSystem.GetDescription(choices[2])}";
        }

        if (GameFlowManager.IsGameOver)
        {
            centerTitleText.text = "Game Over";
            centerBodyText.text = $"{GetGameOverStoryBlurb()}\n\nFinal Score: {ScoreTracker.CurrentScore}\nPress Space or Enter to restart";
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

    private static string GetStageStoryBlurb(int stage)
    {
        // Placeholder story delivery. Good enough for now, easy to graduate later.
        switch (stage)
        {
            case 1:
                return "The first wave breaks through the upper atmosphere before dawn, burning across the sky like falling stars. Earth's conventional squadrons are scattered, and command commits its ace in the hole: you and the experimental fighter prototype. The aircraft is unstable, unproven, and impossibly fast. If it works we can turn back this alien menace.";
            case 2:
                return "The enemy adapts faster than anyone expected, sending heavier craft to crush the resistance before it can organize. Your victories have bought Earth time, but every radar screen is still filling with hostile signatures. Engineers push new combat systems into your fighter between sorties, trusting you to survive the test flight. The war is becoming a race between alien numbers and human invention.";
            case 3:
                return "Alien ace units descend from orbit, hunting the pilot who has become the symbol of Earth's resistance. Cities below go dark to hide from bombardment, while the prototype's reactor is pushed far beyond its safety limits. You are no longer just defending territory; you are holding together the belief that Earth can still win. The next battle will decide whether that belief spreads or dies.";
            case 4:
                return "The alien main force arrives in formation, vast enough to blot out the horizon. Every remaining Earth defense battery opens fire as your fighter launches into the heart of the assault. The prototype was built for one desperate purpose: break the enemy command wave before it reaches the surface. Drive them back here, and Earth survives.";
            default:
                return "The next enemy wave is inbound. Ready the prototype and hold the line. Earth is counting on you.";
        }
    }

    private static string GetFinalStoryBlurb()
    {
        return "The alien command formation fractures under your attack, its surviving ships turning back toward the stars. Across Earth, emergency broadcasts give way to cheering voices as the skies finally clear. The prototype limps home scarred but victorious, proof that humanity's resistance could not be broken. Earth is saved, and its spearhead has become a legend.";
    }

    private static string GetGameOverStoryBlurb()
    {
        return "The prototype falls silent as the last transmission breaks apart in the static. Across the defense network, Earth's remaining squadrons scatter under the weight of the alien advance. For a moment, the sky belongs entirely to the invaders. Humanity's spearhead has fallen, and the resistance must find another way to survive.";
    }
}
