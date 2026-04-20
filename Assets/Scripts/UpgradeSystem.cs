using System.Collections.Generic;
using UnityEngine;

public enum PlayerUpgrade
{
    HomingBullets,
    GatlingMode,
    MissileMode,
    TailGunner,
    Squadron,
    SpecializedTraining,
    StructuralReinforcement,
    EnergyShielding,
    ImprovedAmmunition
}

public static class UpgradeSystem
{
    private static readonly List<PlayerUpgrade> availableUpgrades = new List<PlayerUpgrade>();
    private static readonly List<PlayerUpgrade> selectedUpgrades = new List<PlayerUpgrade>();
    private static readonly PlayerUpgrade[] currentChoices = new PlayerUpgrade[3];

    public static IReadOnlyList<PlayerUpgrade> SelectedUpgrades => selectedUpgrades;
    public static IReadOnlyList<PlayerUpgrade> CurrentChoices => currentChoices;

    public static event System.Action UpgradesChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        StartNewRun();
    }

    public static void StartNewRun()
    {
        availableUpgrades.Clear();
        selectedUpgrades.Clear();

        foreach (PlayerUpgrade upgrade in System.Enum.GetValues(typeof(PlayerUpgrade)))
            availableUpgrades.Add(upgrade);

        ClearChoices();
        UpgradesChanged?.Invoke();
    }

    public static void PrepareChoices()
    {
        ClearChoices();

        for (int i = 0; i < currentChoices.Length && availableUpgrades.Count > 0; i++)
        {
            int choiceIndex = Random.Range(0, availableUpgrades.Count);
            currentChoices[i] = availableUpgrades[choiceIndex];
            availableUpgrades.RemoveAt(choiceIndex);
        }

        UpgradesChanged?.Invoke();
    }

    public static bool SelectCurrentChoice(int choiceIndex)
    {
        if (choiceIndex < 0 || choiceIndex >= currentChoices.Length)
            return false;

        PlayerUpgrade upgrade = currentChoices[choiceIndex];
        if (HasUpgrade(upgrade))
            return false;

        selectedUpgrades.Add(upgrade);
        RemoveMutuallyExclusiveUpgrades(upgrade);
        ClearChoices();
        UpgradesChanged?.Invoke();
        return true;
    }

    public static bool HasUpgrade(PlayerUpgrade upgrade)
    {
        return selectedUpgrades.Contains(upgrade);
    }

    public static string GetDisplayName(PlayerUpgrade upgrade)
    {
        switch (upgrade)
        {
            case PlayerUpgrade.HomingBullets:
                return "Homing Bullets";
            case PlayerUpgrade.GatlingMode:
                return "Gatling Mode";
            case PlayerUpgrade.MissileMode:
                return "Missile Mode";
            case PlayerUpgrade.TailGunner:
                return "Tail Gunner";
            case PlayerUpgrade.Squadron:
                return "Squadron";
            case PlayerUpgrade.SpecializedTraining:
                return "Specialized Training";
            case PlayerUpgrade.StructuralReinforcement:
                return "Structural Reinforcement";
            case PlayerUpgrade.EnergyShielding:
                return "Energy Shielding";
            case PlayerUpgrade.ImprovedAmmunition:
                return "Improved Ammunition";
            default:
                return upgrade.ToString();
        }
    }

    public static string GetDescription(PlayerUpgrade upgrade)
    {
        switch (upgrade)
        {
            case PlayerUpgrade.HomingBullets:
                return "Bullets partially track nearby enemies.";
            case PlayerUpgrade.GatlingMode:
                return "Massive fire rate after a short spin-up.";
            case PlayerUpgrade.MissileMode:
                return "Replaces bullets with slow-firing guided missiles.";
            case PlayerUpgrade.TailGunner:
                return "Rear gun automatically fires behind you.";
            case PlayerUpgrade.Squadron:
                return "Two allied planes join the fight.";
            case PlayerUpgrade.SpecializedTraining:
                return "Faster movement, quicker turning, cloud immunity.";
            case PlayerUpgrade.StructuralReinforcement:
                return "Double hull and regenerate while firing.";
            case PlayerUpgrade.EnergyShielding:
                return "Negate one hit every five seconds.";
            case PlayerUpgrade.ImprovedAmmunition:
                return "More damage, faster shots, less recoil.";
            default:
                return string.Empty;
        }
    }

    private static void RemoveMutuallyExclusiveUpgrades(PlayerUpgrade selectedUpgrade)
    {
        if (selectedUpgrade == PlayerUpgrade.GatlingMode)
            availableUpgrades.Remove(PlayerUpgrade.MissileMode);
        else if (selectedUpgrade == PlayerUpgrade.MissileMode)
            availableUpgrades.Remove(PlayerUpgrade.GatlingMode);
    }

    private static void ClearChoices()
    {
        for (int i = 0; i < currentChoices.Length; i++)
            currentChoices[i] = default;
    }
}
