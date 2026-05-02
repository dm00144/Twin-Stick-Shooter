using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StageProgression
{
    public const int MaxStage = 4;
    public const int StartingLives = 3;

    private static readonly List<string> chosenUpgrades = new List<string>();

    public static int CurrentStage { get; private set; } = 1;
    public static int LivesRemaining { get; private set; } = StartingLives;
    public static int StageStartScore { get; private set; }
    public static int CurrentStageScore => Mathf.Max(0, ScoreTracker.CurrentScore - StageStartScore);
    public static IReadOnlyList<string> ChosenUpgrades => chosenUpgrades;

    public static event System.Action ChoicesChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        StartNewRun();
    }

    public static void StartNewRun()
    {
        // New run, clean slate. The prototype gets one more shot at being a good idea.
        CurrentStage = 1;
        LivesRemaining = StartingLives;
        StageStartScore = ScoreTracker.CurrentScore;
        chosenUpgrades.Clear();
        UpgradeSystem.StartNewRun();
        ChoicesChanged?.Invoke();
    }

    public static void HandleBossDestroyed()
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        // The last boss defeat gets a story beat before the regular victory screen kicks in.
        if (CurrentStage >= MaxStage)
        {
            GameFlowManager.TriggerFinalStoryBlurb();
            return;
        }

        PrepareUpgradeChoices();
        GameFlowManager.TriggerUpgradeChoice();
    }

    public static void SelectUpgrade(int choiceIndex)
    {
        if (!GameFlowManager.IsUpgradeChoiceActive)
            return;

        if (choiceIndex < 0 || choiceIndex >= UpgradeSystem.CurrentChoices.Count)
            return;

        PlayerUpgrade upgrade = UpgradeSystem.CurrentChoices[choiceIndex];
        if (!UpgradeSystem.SelectCurrentChoice(choiceIndex))
            return;

        chosenUpgrades.Add(UpgradeSystem.GetDisplayName(upgrade));
        CurrentStage = Mathf.Min(MaxStage, CurrentStage + 1);
        StageStartScore = ScoreTracker.CurrentScore;
        // Between stages we wipe bullets/enemies and park the player back at the starting mark.
        StageSoftReset.ResetForNextStage();
        GameFlowManager.ShowStageStoryBlurb();
        ChoicesChanged?.Invoke();
    }

    public static bool TrySpendLife()
    {
        if (LivesRemaining <= 1)
        {
            LivesRemaining = 0;
            ChoicesChanged?.Invoke();
            return false;
        }

        LivesRemaining--;
        StageStartScore = ScoreTracker.CurrentScore;
        // A lost life restarts the current fight, not the whole war.
        StageSoftReset.ResetForNextStage();
        ChoicesChanged?.Invoke();
        return true;
    }

    public static int GetCurrentStageGoal()
    {
        return MaxStage;
    }

    private static void PrepareUpgradeChoices()
    {
        UpgradeSystem.PrepareChoices();
        ChoicesChanged?.Invoke();
    }
}

public static class StageSoftReset
{
    public static void ResetForNextStage()
    {
        // Clear the stage clutter so old bullets do not follow the player into the next scene beat.
        EnemyAI.DestroyAllActiveEnemies();
        BulletPewPew.DestroyAllActiveBullets();

        PlayerMovement player = Object.FindFirstObjectByType<PlayerMovement>();
        if (player != null)
            player.ResetForStageStart();

        SquadronManager.RefreshSquadron();
    }
}

public class StageProgressionDirector : MonoBehaviour
{
    private static StageProgressionDirector instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject directorObject = new GameObject("StageProgressionDirector");
        instance = directorObject.AddComponent<StageProgressionDirector>();
        Object.DontDestroyOnLoad(directorObject);
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
        StageProgression.ChoicesChanged += ApplyStageVisuals;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        StageProgression.ChoicesChanged -= ApplyStageVisuals;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Update()
    {
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyStageVisuals();
    }

    private void ApplyStageVisuals()
    {
        // Backgrounds are just the sprite renderers tucked behind the action with sortingOrder -1.
        SpriteRenderer[] renderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.sortingOrder != -1)
                continue;

            renderer.sprite = GameSpriteLibrary.GetStageBackgroundSprite(StageProgression.CurrentStage);
            renderer.color = Color.white;
        }
    }
}
