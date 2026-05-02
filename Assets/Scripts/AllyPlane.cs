using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class AllyPlane : MonoBehaviour
{
    [SerializeField] private float followAcceleration = 420f;
    [SerializeField] private float maxSpeed = 360f;
    [SerializeField] private float gravityAcceleration = 8f;
    [SerializeField] private float fireCooldown = 0.18f;
    [SerializeField] private float bulletLifeTime = 3.2f;
    [SerializeField] private float fireSpreadAngle = 8f;
    [SerializeField] private float contactInvulnerability = 1f;
    [SerializeField] private float healthMultiplier = 3.5f;
    [SerializeField] private float screenPadding = 6f;

    private Rigidbody2D rb;
    private PlayerMovement player;
    private Vector3 formationOffset;
    private float currentHealth;
    private float nextFireTime;
    private float lastHitTime = float.NegativeInfinity;
    private float lastCombatActionTime = float.NegativeInfinity;
    private SpriteRenderer spriteRenderer;

    public void Initialize(PlayerMovement owner, Vector3 offset)
    {
        // Allies are basically remote wingmen glued to a formation slot behind the player.
        player = owner;
        formationOffset = offset;
        currentHealth = GetMaxHealth();
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        Collider2D allyCollider = GetComponent<Collider2D>();
        allyCollider.isTrigger = false;
    }

    private void Update()
    {
        if (!GameFlowManager.IsGameplayActive || player == null)
            return;

        // They are independent enough to shoot, but not independent enough to make life choices.
        TryFireAtNearestEnemy();

        float maxHealth = GetMaxHealth();
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;

        bool canHeal = currentHealth < maxHealth && Time.time >= lastCombatActionTime + 2.5f;
        if (canHeal)
            currentHealth = Mathf.Min(maxHealth, currentHealth + Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!GameFlowManager.IsGameplayActive || player == null)
        {
            // Drift down gently while the player is reading menus or between stages.
            rb.linearVelocity *= 0.98f;
            rb.angularVelocity *= 0.98f;
            return;
        }

        Vector3 targetPosition = ClampToCameraView(player.transform.TransformPoint(formationOffset));
        targetPosition = ClampToWorldBounds(targetPosition);
        // Formation flying by acceleration keeps them lively instead of perfectly nailed to the player.
        Vector2 toTarget = targetPosition - transform.position;
        rb.linearVelocity += toTarget.normalized * (followAcceleration * Time.fixedDeltaTime);
        rb.linearVelocity += Vector2.down * gravityAcceleration * Time.fixedDeltaTime;

        if (rb.linearVelocity.magnitude > maxSpeed)
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;

        EnemyAI target = FindNearestEnemy();
        Vector2 aimDirection = target != null
            ? ((Vector2)target.transform.position - rb.position).normalized
            : (Vector2)player.transform.up;

        float targetAngle = Mathf.Atan2(aimDirection.y, aimDirection.x) * Mathf.Rad2Deg - 90f;
        rb.MoveRotation(Mathf.MoveTowardsAngle(rb.rotation, targetAngle, 540f * Time.fixedDeltaTime));
    }

    public void TakeHit(int damage = 1)
    {
        if (Time.time < lastHitTime + contactInvulnerability)
            return;

        // Allies can take a few hits, but they are absolutely not main-character material.
        lastHitTime = Time.time;
        lastCombatActionTime = Time.time;
        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (currentHealth <= 0f)
            Destroy(gameObject);
    }

    private void TryFireAtNearestEnemy()
    {
        if (Time.time < nextFireTime)
            return;

        EnemyAI target = FindNearestEnemy();
        if (target == null)
            return;

        // Wingmen pick the closest target and add a little spread so they feel less robotic.
        Vector2 fireDirection = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        fireDirection = Quaternion.Euler(0f, 0f, Random.Range(-fireSpreadAngle, fireSpreadAngle)) * fireDirection;

        BulletPewPew bullet = CreateBullet();
        bullet.transform.position = GetMuzzlePosition();
        bullet.transform.rotation = Quaternion.FromToRotation(Vector3.up, fireDirection);
        bullet.SetLifeTime(bulletLifeTime);
        bullet.ConfigureDamage(player.EffectiveBulletDamage);
        bullet.ConfigureHoming(player.EffectiveBulletHomingStrength, true);
        bullet.Initialize(fireDirection, Vector2.zero, gameObject, player.EffectiveBulletSpeed);

        GameAudio.PlaySquadronGun();
        nextFireTime = Time.time + (player.UsesMissiles ? 1.1f : fireCooldown);
        lastCombatActionTime = Time.time;
    }

    private BulletPewPew CreateBullet()
    {
        if (player != null && player.UsesMissiles)
        {
            // Squadron inherits missile mode too. Experimental tech, shared irresponsibly.
            return BulletPewPew.CreateRuntimeBullet(
                "AllyMissile",
                new Color(1f, 0.5f, 0.2f, 1f),
                new Vector3(1.7f, 3.4f, 1f));
        }

        return BulletPewPew.CreateRuntimeBullet(
            "AllyBullet",
            new Color(0.55f, 0.9f, 1f, 1f),
            new Vector3(1f, 1.8f, 1f));
    }

    private Vector3 GetMuzzlePosition()
    {
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            float spriteHalfHeight = spriteRenderer.sprite.bounds.extents.y * transform.lossyScale.y;
            return transform.position + transform.up * spriteHalfHeight * 0.92f;
        }

        return transform.position + transform.up * 1.5f;
    }

    private EnemyAI FindNearestEnemy()
    {
        // This is brute-force and fine for the current enemy counts. No need to summon a targeting system yet.
        EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        EnemyAI bestEnemy = null;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null)
                continue;

            float distanceSqr = ((Vector2)enemies[i].transform.position - rb.position).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
                continue;

            bestDistanceSqr = distanceSqr;
            bestEnemy = enemies[i];
        }

        return bestEnemy;
    }

    private float GetMaxHealth()
    {
        return (player != null ? player.MaxHealth : 3f) * healthMultiplier;
    }

    private Vector3 ClampToCameraView(Vector3 targetPosition)
    {
        Camera camera = Camera.main;
        if (camera == null || !camera.orthographic)
            return targetPosition;

        // Keep wingmen visible. Off-screen allies are not heroic, just confusing.
        float halfHeight = camera.orthographicSize;
        float halfWidth = halfHeight * camera.aspect;
        Vector3 cameraPosition = camera.transform.position;

        targetPosition.x = Mathf.Clamp(
            targetPosition.x,
            cameraPosition.x - halfWidth + screenPadding,
            cameraPosition.x + halfWidth - screenPadding);
        targetPosition.y = Mathf.Clamp(
            targetPosition.y,
            cameraPosition.y - halfHeight + screenPadding,
            cameraPosition.y + halfHeight - screenPadding);

        return targetPosition;
    }

    private Vector3 ClampToWorldBounds(Vector3 targetPosition)
    {
        BoxCollider2D boundsCollider = ResolveBoundsCollider();
        if (boundsCollider == null)
            return targetPosition;

        Bounds bounds = boundsCollider.bounds;
        targetPosition.x = Mathf.Clamp(targetPosition.x, bounds.min.x + screenPadding, bounds.max.x - screenPadding);
        targetPosition.y = Mathf.Clamp(targetPosition.y, bounds.min.y + screenPadding, bounds.max.y - screenPadding);
        return targetPosition;
    }

    private static BoxCollider2D ResolveBoundsCollider()
    {
        CameraTracker tracker = FindFirstObjectByType<CameraTracker>();
        if (tracker != null && tracker.BoundsCollider != null)
            return tracker.BoundsCollider;

        GameObject boundsObject = GameObject.Find("Camera Bounds");
        return boundsObject != null ? boundsObject.GetComponent<BoxCollider2D>() : null;
    }
}

public static class SquadronManager
{
    public static void RefreshSquadron()
    {
        // Rebuild instead of trying to patch the formation. Cleaner, and the squadron is tiny.
        DestroySquadron();

        if (!UpgradeSystem.HasUpgrade(PlayerUpgrade.Squadron))
            return;

        PlayerMovement player = Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null)
            return;

        CreateAlly(player, new Vector3(-7f, -8f, 0f), 1);
        CreateAlly(player, new Vector3(7f, -8f, 0f), 2);
    }

    public static void DestroySquadron()
    {
        AllyPlane[] allies = Object.FindObjectsByType<AllyPlane>(FindObjectsSortMode.None);
        for (int i = 0; i < allies.Length; i++)
        {
            if (allies[i] != null)
                Object.Destroy(allies[i].gameObject);
        }
    }

    private static void CreateAlly(PlayerMovement player, Vector3 offset, int index)
    {
        // Runtime-built allies match the rest of this prototype: practical first, fancy later.
        GameObject allyObject = new GameObject($"SquadronAlly_{index}");
        allyObject.transform.position = player.transform.TransformPoint(offset);
        allyObject.transform.rotation = player.transform.rotation;
        allyObject.transform.localScale = new Vector3(3f, 3f, 1f);

        SpriteRenderer renderer = allyObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GameSpriteLibrary.GetSquadronShipSprite();
        renderer.color = Color.white;
        renderer.sortingOrder = 8;

        Rigidbody2D body = allyObject.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;

        CircleCollider2D collider = allyObject.AddComponent<CircleCollider2D>();
        if (renderer.sprite != null)
            collider.radius = Mathf.Min(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y) * allyObject.transform.localScale.x * 0.18f;
        else
            collider.radius = 0.45f;

        AllyPlane ally = allyObject.AddComponent<AllyPlane>();
        ally.Initialize(player, offset);
    }
}
