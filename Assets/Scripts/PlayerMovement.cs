using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float acceleration = 1040f;
    [SerializeField] private float maxSpeed = 480f;
    [SerializeField] private float idleDamping = 0.999f;
    [SerializeField] private float gravityAcceleration = 8f;

    [Header("Turning")]
    [SerializeField] private float rotationSpeed = 1440f;

    [Header("Combat")]
    [SerializeField] private BulletPewPew bulletPrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float muzzleDistance = 1.6f;
    [SerializeField] private float bulletSpeed = 360f;
    [SerializeField] private float bulletLifeTime = 3.6f;
    [SerializeField] private float fireCooldown = 0.08f;
    [SerializeField] private float bulletSpreadAngle = 3f;
    [SerializeField] private float recoilForce = 2.5f;
    [SerializeField] private float gatlingSpinUpTime = 0.4f;
    [SerializeField] private float tailGunnerCooldown = 0.117f;
    [SerializeField] private float tailGunnerSpreadAngle = 6f;

    [Header("Health")]
    [SerializeField] private float maxHealth = 3f;
    [SerializeField] private float regenDelay = 2.5f;
    [SerializeField] private float regenRate = 1f;
    [SerializeField] private float contactInvulnerability = 1f;
    [SerializeField] private float shieldRechargeTime = 5f;

    private Rigidbody2D rb;
    private float turnInput;
    private float thrustInput;
    private float currentHealth;
    private float nextFireTime;
    private float lastHitTime = float.NegativeInfinity;
    private float lastCombatActionTime = float.NegativeInfinity;
    private float shootHeldTime;
    private float nextTailGunnerFireTime;
    private float lastShieldBlockTime = float.NegativeInfinity;
    private Vector3 stageStartPosition;
    private Quaternion stageStartRotation;
    private readonly Dictionary<int, float> movementModifiers = new Dictionary<int, float>();

    public float CurrentHealth => currentHealth;
    public float MaxHealth => EffectiveMaxHealth;
    public float HealthNormalized => EffectiveMaxHealth <= 0f ? 0f : currentHealth / EffectiveMaxHealth;
    public bool IsDead => currentHealth <= 0f;
    public float EffectiveBulletSpeed => GetEffectiveBulletSpeed();
    public int EffectiveBulletDamage => GetEffectiveBulletDamage();
    public float EffectiveBulletHomingStrength => GetEffectiveBulletHomingStrength();
    public bool UsesMissiles => UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ApplyPlayerSprite();
        currentHealth = maxHealth;

        // We drive the plane manually, so Unity gravity gets politely shown the door.
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        stageStartPosition = transform.position;
        stageStartRotation = transform.rotation;
    }

    private void ApplyPlayerSprite()
    {
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            return;

        Sprite playerSprite = GameSpriteLibrary.GetPlayerShipSprite();
        if (playerSprite == null)
            return;

        spriteRenderer.sprite = playerSprite;
        spriteRenderer.color = Color.white;
        AlignColliderToSprite(playerSprite);
    }

    private void AlignColliderToSprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        BoxCollider2D collider = GetComponent<BoxCollider2D>();
        if (collider == null)
            return;

        Vector2 spriteSize = sprite.bounds.size;
        collider.size = new Vector2(spriteSize.x * 0.55f, spriteSize.y * 0.72f);
        collider.offset = new Vector2(0f, spriteSize.y * 0.02f);
    }

    private void Update()
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        // Read input in Update, then let FixedUpdate do the actual shoving around.
        turnInput = 0f;
        thrustInput = 0f;

        var keyboard = Keyboard.current;
        bool isTryingToShoot = false;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed)
                turnInput = 1f;
            else if (keyboard.dKey.isPressed)
                turnInput = -1f;

            if (keyboard.wKey.isPressed)
                thrustInput = 1f;

            isTryingToShoot = keyboard.spaceKey.isPressed;
            if (isTryingToShoot)
                TryShoot();
        }

        if (!isTryingToShoot)
            shootHeldTime = 0f;

        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.TailGunner))
            TryTailGunner();

        // Shooting and taking hits interrupt regen, because duct tape needs a quiet moment.
        float effectiveMaxHealth = EffectiveMaxHealth;
        if (currentHealth > effectiveMaxHealth)
            currentHealth = effectiveMaxHealth;

        bool ignoresCombatRegenDelay = UpgradeSystem.HasUpgrade(PlayerUpgrade.StructuralReinforcement);
        bool canHeal = currentHealth < effectiveMaxHealth && (ignoresCombatRegenDelay || Time.time >= lastCombatActionTime + regenDelay);
        if (canHeal)
        {
            currentHealth = Mathf.Min(effectiveMaxHealth, currentHealth + regenRate * Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (!GameFlowManager.IsGameplayActive)
        {
            // Let the ship coast down when menus/story cards pause the dogfight.
            rb.linearVelocity *= 0.98f;
            rb.angularVelocity *= 0.98f;
            return;
        }

        var slow = GetMovementMultiplier();
        float movementSpeedMultiplier = UpgradeSystem.HasUpgrade(PlayerUpgrade.SpecializedTraining) ? 1.25f : 1f;
        float turnSpeedMultiplier = UpgradeSystem.HasUpgrade(PlayerUpgrade.SpecializedTraining) ? 1.25f : 1f;

        rb.MoveRotation(rb.rotation + turnInput * rotationSpeed * turnSpeedMultiplier * Time.fixedDeltaTime);

        if (thrustInput > 0f)
            rb.linearVelocity += (Vector2)transform.up * (acceleration * movementSpeedMultiplier * slow * Time.fixedDeltaTime);
        else
            rb.linearVelocity *= idleDamping;

        // The whole battle drifts downward, so the player is always fighting the sky a little.
        rb.linearVelocity += Vector2.down * gravityAcceleration * slow * Time.fixedDeltaTime;

        var speedCap = maxSpeed * movementSpeedMultiplier * slow;
        if (rb.linearVelocity.magnitude <= speedCap) return;

        rb.linearVelocity = rb.linearVelocity.normalized * speedCap;
    }

    public void SetMovementModifier(int sourceId, float multiplier)
    {
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.SpecializedTraining))
            return;

        // Multiple clouds can slow us at once; keep each source separate so it can clean itself up.
        movementModifiers[sourceId] = Mathf.Clamp(multiplier, 0.1f, 1f);
    }

    public void ClearMovementModifier(int sourceId)
    {
        movementModifiers.Remove(sourceId);
    }

    public void TakeHit(int damage = 1)
    {
        if (Time.time < lastHitTime + contactInvulnerability)
            return;

        if (TryBlockWithShield())
        {
            // Shield upgrade eats the hit, then goes off to recharge and think about what it did.
            lastHitTime = Time.time;
            return;
        }

        lastHitTime = Time.time;
        lastCombatActionTime = Time.time;
        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (currentHealth <= 0f)
            HandleDeath();
    }

    public void ResetForStageStart()
    {
        // Stage reset is a soft reset: keep upgrades, restore the aircraft, clear the chaos.
        transform.SetPositionAndRotation(stageStartPosition, stageStartRotation);
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        movementModifiers.Clear();
        shootHeldTime = 0f;
        currentHealth = EffectiveMaxHealth;
    }

    private void TryShoot()
    {
        shootHeldTime += Time.deltaTime;

        // Gatling mode needs a tiny wind-up before it becomes deeply unreasonable.
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.GatlingMode) && shootHeldTime < gatlingSpinUpTime)
            return;

        if (Time.time < nextFireTime)
            return;

        var bullet = CreateBulletInstance();
        if (bullet == null)
            return;

        float spreadOffset = Random.Range(-bulletSpreadAngle, bulletSpreadAngle);
        Vector2 fireDirection = Quaternion.Euler(0f, 0f, spreadOffset) * transform.up;
        Vector3 spawnPoint = GetMuzzlePosition(true);

        bullet.transform.position = spawnPoint;
        bullet.transform.rotation = transform.rotation;
        bullet.SetLifeTime(UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode) ? bulletLifeTime * 1.6f : bulletLifeTime);
        bullet.ConfigureDamage(GetEffectiveBulletDamage());
        bullet.ConfigureHoming(GetEffectiveBulletHomingStrength(), true);
        bullet.Initialize(fireDirection, Vector2.zero, gameObject, GetEffectiveBulletSpeed());

        // Recoil inspired by Luftrausers: shooting is also a tiny movement choice.
        rb.AddForce(-fireDirection * GetEffectiveRecoilForce(), ForceMode2D.Impulse);
        GameAudio.PlayPlayerGun();
        nextFireTime = Time.time + GetEffectiveFireCooldown();
        lastCombatActionTime = Time.time;
    }

    private void HandleDeath()
    {
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        GameFlowManager.TriggerPlayerDeath();
    }

    private float GetMovementMultiplier()
    {
        float multiplier = 1f;
        foreach (var value in movementModifiers.Values)
            multiplier = Mathf.Min(multiplier, value);

        return multiplier;
    }

    private void TryTailGunner()
    {
        if (Time.time < nextTailGunnerFireTime)
            return;

        // Tail gunner only cares about enemies behind us. Very loyal, very literal.
        EnemyAI target = FindTailGunnerTarget();
        if (target == null)
            return;

        var bullet = CreateBulletInstance();
        if (bullet == null)
            return;

        Vector2 rearDirection = -transform.up;
        Vector2 targetDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        float spreadOffset = Random.Range(-tailGunnerSpreadAngle, tailGunnerSpreadAngle);
        Vector2 fireDirection = Quaternion.Euler(0f, 0f, spreadOffset) * Vector2.Lerp(rearDirection, targetDirection, 0.85f).normalized;
        Vector3 spawnPoint = GetMuzzlePosition(false);

        bullet.transform.position = spawnPoint;
        bullet.transform.rotation = Quaternion.FromToRotation(Vector3.up, fireDirection);
        bullet.SetLifeTime(bulletLifeTime);
        bullet.ConfigureDamage(GetEffectiveBulletDamage());
        bullet.ConfigureHoming(GetEffectiveBulletHomingStrength() * 0.5f, true);
        bullet.Initialize(fireDirection, Vector2.zero, gameObject, GetEffectiveBulletSpeed() * 0.85f);

        GameAudio.PlaySquadronGun();
        nextTailGunnerFireTime = Time.time + tailGunnerCooldown;
    }

    private EnemyAI FindTailGunnerTarget()
    {
        EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        EnemyAI bestEnemy = null;
        float bestDistanceSqr = float.PositiveInfinity;
        Vector2 rearDirection = -transform.up;

        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null)
                continue;

            Vector2 toEnemy = enemies[i].transform.position - transform.position;
            if (Vector2.Dot(rearDirection, toEnemy.normalized) < 0.35f)
                continue;

            float distanceSqr = toEnemy.sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestEnemy = enemies[i];
        }

        return bestEnemy;
    }

    private bool TryBlockWithShield()
    {
        if (!UpgradeSystem.HasUpgrade(PlayerUpgrade.EnergyShielding))
            return false;

        if (Time.time < lastShieldBlockTime + shieldRechargeTime)
            return false;

        lastShieldBlockTime = Time.time;
        return true;
    }

    private float EffectiveMaxHealth => UpgradeSystem.HasUpgrade(PlayerUpgrade.StructuralReinforcement) ? maxHealth * 2f : maxHealth;

    private float GetEffectiveFireCooldown()
    {
        // Upgrade math lives here so the firing code can stay readable.
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode))
            return 0.85f;

        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.GatlingMode))
            return 0.012f;

        return fireCooldown;
    }

    private float GetEffectiveBulletSpeed()
    {
        float effectiveSpeed = UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode) ? bulletSpeed * 0.55f : bulletSpeed;
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.ImprovedAmmunition))
            effectiveSpeed *= 1.25f;

        return effectiveSpeed;
    }

    private int GetEffectiveBulletDamage()
    {
        int effectiveDamage = UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode) ? 8 : 1;
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.ImprovedAmmunition))
            effectiveDamage += UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode) ? 4 : 1;

        return effectiveDamage;
    }

    private float GetEffectiveBulletHomingStrength()
    {
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode))
            return 5.5f;

        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.HomingBullets))
            return 1.7f;

        return 0f;
    }

    private float GetEffectiveRecoilForce()
    {
        float effectiveRecoil = UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode) ? recoilForce * 1.5f : recoilForce;
        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.GatlingMode))
            effectiveRecoil *= 0.18f;

        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.ImprovedAmmunition))
            effectiveRecoil *= 0.55f;

        return effectiveRecoil;
    }

    private Vector3 GetMuzzlePosition(bool forward)
    {
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            float spriteHalfHeight = spriteRenderer.sprite.bounds.extents.y * transform.lossyScale.y;
            float direction = forward ? 1f : -1f;
            return transform.position + transform.up * spriteHalfHeight * 0.92f * direction;
        }

        if (firePoint != null && forward)
            return firePoint.position;

        return transform.position + transform.up * muzzleDistance * (forward ? 1f : -1f);
    }

    private BulletPewPew CreateBulletInstance()
    {
        // Prefer scene/prefab bullets if they exist, otherwise build a simple runtime bullet.
        if (bulletPrefab != null)
        {
            var bullet = Instantiate(bulletPrefab);
            bullet.transform.localScale = UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode)
                ? new Vector3(2f, 4f, 1f)
                : new Vector3(1.1f, 2f, 1f);
            return bullet;
        }

        if (UpgradeSystem.HasUpgrade(PlayerUpgrade.MissileMode))
        {
            return BulletPewPew.CreateRuntimeBullet(
                "PlayerMissile",
                new Color(1f, 0.42f, 0.18f, 1f),
                new Vector3(2f, 4f, 1f));
        }

        return BulletPewPew.CreateRuntimeBullet(
            "PlayerBullet",
            new Color(1f, 0.92f, 0.3f, 1f),
            new Vector3(1.1f, 2f, 1f));
    }
}
