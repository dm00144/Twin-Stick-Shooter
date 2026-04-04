using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyAI : MonoBehaviour
{
    private enum EnemyVariant
    {
        Fighter,
        SpreadShooter,
        Ace,
        Boss
    }

    private static EnemyAI spawnTemplate;
    private static int activeEnemyCount;
    private static bool isMakingTemplate;
    private static bool bossSpawned;
    private static bool bossAlive;

    [Header("Variant")]
    [SerializeField] private EnemyVariant variant = EnemyVariant.Fighter;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float turnSpeed = 360f;
    [SerializeField] private float preferredDistance = 26f;
    [SerializeField] private float gravityAcceleration = 6f;

    [Header("Combat")]
    [SerializeField] private int hitPoints = 1;
    [SerializeField] private BulletPewPew bulletPrefab;
    [SerializeField] private float bulletSpeed = 60f;
    [SerializeField] private float fireCooldown = 3.4f;
    [SerializeField] private float fireStartDelay = 3.25f;
    [SerializeField] private float bulletSpawnOffset = 1.2f;
    [SerializeField] private float bulletSpreadAngle = 3f;
    [SerializeField] private int bulletsPerShot = 1;
    [SerializeField] private float multiShotSpacing = 10f;
    [SerializeField] private int scoreValue = 100;

    [Header("Spawning")]
    [SerializeField] private int initialEnemyCount = 3;
    [SerializeField] private float minSpawnDistance = 150f;
    [SerializeField] private float maxSpawnDistance = 260f;
    [SerializeField] private bool skipInitialWaveSpawn;

    private static bool initialWaveSpawned;
    private Transform player;
    private Rigidbody2D rb;
    private float nextFireTime;
    private bool isDead;
    private SpriteRenderer spriteRenderer;
    private int attackPatternIndex;

    public static int ActiveEnemyCount => activeEnemyCount;
    public static bool BossAlive => bossAlive;
    public static bool BossSpawned => bossSpawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        spawnTemplate = null;
        activeEnemyCount = 0;
        isMakingTemplate = false;
        bossSpawned = false;
        bossAlive = false;
        initialWaveSpawned = false;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;

        // Presets for enemy clases
        ApplyVariantPreset();

        EnsureSpawnTemplateExists();

        if (!isMakingTemplate)
            activeEnemyCount++;

        if (variant == EnemyVariant.Boss && !isMakingTemplate)
        {
            bossSpawned = true;
            bossAlive = true;
        }
    }

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        SpawnInitialWaveIfNeeded();
    }

    private void FixedUpdate()
    {
        if (!GameFlowManager.IsGameplayActive)
        {
            // Enemies should stop pretty qucik when gameplay ends
            rb.linearVelocity *= 0.95f;
            return;
        }

        if (player == null)
            return;

        var toPlayer = player.position - transform.position;
        float distanceToPlayer = toPlayer.magnitude;
        Vector2 aim = distanceToPlayer > 0.01f ? (Vector2)(toPlayer / distanceToPlayer) : Vector2.zero;

        float targetAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90f;
        float nextAngle = Mathf.MoveTowardsAngle(rb.rotation, targetAngle, turnSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(nextAngle);

        // Stay near the preferred band around the player rather than bee-lining all the time.
        float distanceError = distanceToPlayer - preferredDistance;
        var moveDirection = Mathf.Abs(distanceError) < 3f ? 0f : Mathf.Sign(distanceError);
        rb.linearVelocity = aim * (moveSpeed * moveDirection);
        rb.linearVelocity += Vector2.down * gravityAcceleration * Time.fixedDeltaTime;

        if (Time.time >= nextFireTime && distanceToPlayer <= maxSpawnDistance)
            FireAtPlayer(aim);
    }

    public void TakeDamage(int damage)
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        if (isDead)
            return;

        hitPoints -= damage;
        if (hitPoints <= 0)
        {
            isDead = true;
            ScoreTracker.Add(scoreValue);
            if (variant == EnemyVariant.Boss)
                GameFlowManager.TriggerVictory();
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (gameObject.scene.IsValid())
            activeEnemyCount = Mathf.Max(0, activeEnemyCount - 1);

        if (variant == EnemyVariant.Boss && gameObject.scene.IsValid())
            bossAlive = false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        var playerMovement = collision.collider.GetComponent<PlayerMovement>();
        if (playerMovement == null)
            return;

        playerMovement.TakeHit();

        if (variant != EnemyVariant.Boss)
            Destroy(gameObject);
    }

    private void SpawnInitialWaveIfNeeded()
    {
        if (skipInitialWaveSpawn || initialWaveSpawned || player == null) return;

        initialWaveSpawned = true;
        transform.position = GetSpawnPosition();

        for (int i = 1; i < initialEnemyCount; i++) {
            SpawnEnemyAt(GetSpawnPosition());
        }
    }

    private Vector2 GetSpawnPosition()
    {
        return GetSpawnPositionFor(player);
    }

    private void EnsureSpawnTemplateExists()
    {
        if (isMakingTemplate || spawnTemplate != null)
            return;

        // Spawning template that hopefully will work eventually
        isMakingTemplate = true;
        spawnTemplate = Instantiate(this);
        spawnTemplate.gameObject.name = "EnemySpawnTemplate";
        spawnTemplate.skipInitialWaveSpawn = true;
        spawnTemplate.enabled = false;
        spawnTemplate.gameObject.SetActive(false);
        DontDestroyOnLoad(spawnTemplate.gameObject);
        isMakingTemplate = false;
        skipInitialWaveSpawn = false;
    }

    private void FireAtPlayer(Vector2 direction)
    {
        var spawnPoint = transform.position + transform.up * bulletSpawnOffset;

        if (variant == EnemyVariant.Boss)
        {
            FireBossPattern(direction, spawnPoint);
            nextFireTime = Time.time + fireCooldown;
            return;
        }

        var centeredOffset = (bulletsPerShot - 1) * 0.5f;

        for (int i = 0; i < bulletsPerShot; i++)
        {
            var bullet = CreateBulletInstance();
            if (bullet == null)
                continue;

            float patternOffset = (i - centeredOffset) * multiShotSpacing;
            float spreadOffset = Random.Range(-bulletSpreadAngle, bulletSpreadAngle);
            Vector2 fireDirection = Quaternion.Euler(0f, 0f, patternOffset + spreadOffset) * direction;
            bullet.transform.position = spawnPoint;
            bullet.transform.rotation = Quaternion.FromToRotation(Vector3.up, fireDirection);
            bullet.Initialize(fireDirection, Vector2.zero, gameObject, bulletSpeed);
        }

        nextFireTime = Time.time + fireCooldown;
    }

    private BulletPewPew CreateBulletInstance()
    {
        if (bulletPrefab != null)
        {
            BulletPewPew prefabBullet = Instantiate(bulletPrefab);
            prefabBullet.transform.localScale = new Vector3(0.95f, 1.7f, 1f);
            return prefabBullet;
        }

        return BulletPewPew.CreateRuntimeBullet(
            "EnemyBullet",
            new Color(1f, 0.35f, 0.25f, 1f),
            new Vector3(0.95f, 1.7f, 1f),
            9);
    }

    public static bool TrySpawnEnemy(Transform playerTransform)
    {
        if (spawnTemplate == null || playerTransform == null)
            return false;

        Vector2 spawnPosition = spawnTemplate.GetSpawnPositionFor(playerTransform);
        var spawnVariant = ChooseVariant();
        spawnTemplate.SpawnEnemyAt(spawnPosition, spawnVariant);
        return true;
    }

    public static bool TrySpawnBoss(Transform playerTransform)
    {
        if (spawnTemplate == null || playerTransform == null || bossSpawned)
            return false;

        Vector2 spawnPosition = spawnTemplate.GetSpawnPositionFor(playerTransform);
        spawnTemplate.SpawnEnemyAt(spawnPosition, EnemyVariant.Boss);
        return true;
    }

    private EnemyAI SpawnEnemyAt(Vector2 spawnPosition)
    {
        return SpawnEnemyAt(spawnPosition, variant);
    }

    private EnemyAI SpawnEnemyAt(Vector2 spawnPosition, EnemyVariant spawnVariant)
    {
        EnemyAI clone = Instantiate(this, spawnPosition, Quaternion.identity);
        clone.variant = spawnVariant;
        clone.skipInitialWaveSpawn = true;
        clone.enabled = true;
        clone.gameObject.SetActive(true);
        return clone;
    }

    private Vector2 GetSpawnPositionFor(Transform playerTransform)
    {
        var boundsCollider = ResolveSpawnBounds();
        Vector2 fallback = playerTransform.position;

        // Should make enemys spawn in a ring rather than right ontop of the player initially
        for (int attempt = 0; attempt < 24; attempt++)
        {
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var distance = Random.Range(minSpawnDistance, maxSpawnDistance);
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            Vector2 candidate = (Vector2)playerTransform.position + offset;

            if (boundsCollider == null || boundsCollider.bounds.Contains(candidate))
                return candidate;

            fallback = new Vector2(
                Mathf.Clamp(candidate.x, boundsCollider.bounds.min.x, boundsCollider.bounds.max.x),
                Mathf.Clamp(candidate.y, boundsCollider.bounds.min.y, boundsCollider.bounds.max.y));
        }

        return fallback;
    }

    private static BoxCollider2D ResolveSpawnBounds()
    {
        CameraTracker tracker = FindFirstObjectByType<CameraTracker>();
        if (tracker != null && tracker.BoundsCollider != null)
            return tracker.BoundsCollider;

        GameObject boundsObject = GameObject.Find("Camera Bounds");
        return boundsObject != null ? boundsObject.GetComponent<BoxCollider2D>() : null;
    }

    private void ApplyVariantPreset()
    {
        switch (variant)
        {
            case EnemyVariant.SpreadShooter:
                moveSpeed = 3.8f;
                turnSpeed = 240f;
                preferredDistance = 38f;
                gravityAcceleration = 5f;
                hitPoints = 2;
                bulletSpeed = 52f;
                fireCooldown = 4.2f;
                fireStartDelay = 3.5f;
                bulletSpreadAngle = 2f;
                bulletsPerShot = 3;
                multiShotSpacing = 12f;
                scoreValue = 200;
                TintSprite(new Color(0.35f, 0.95f, 0.45f, 1f));
                break;

            case EnemyVariant.Ace:
                moveSpeed = 35f;
                turnSpeed = 640f;
                preferredDistance = 5f;
                gravityAcceleration = 7f;
                hitPoints = 4;
                bulletSpeed = 90f;
                fireCooldown = .3f;
                fireStartDelay = 2f;
                bulletSpreadAngle = 6f;
                bulletsPerShot = 1;
                multiShotSpacing = 0f;
                scoreValue = 350;
                TintSprite(new Color(1f, 0.55f, 0.15f, 1f));
                break;

            case EnemyVariant.Boss:
                moveSpeed = 30f;
                turnSpeed = 180f;
                preferredDistance = 20f;
                gravityAcceleration = 4f;
                hitPoints = 60;
                bulletSpeed = 180f;
                fireCooldown = 0.35f;
                fireStartDelay = 1.5f;
                bulletSpreadAngle = 2f;
                bulletsPerShot = 1;
                multiShotSpacing = 2f;
                scoreValue = 2000;
                transform.localScale = new Vector3(15f, 24f, 1f);
                TintSprite(new Color(0.95f, 0.35f, 0.2f, 1f));
                break;

            default:
                moveSpeed = 5.5f;
                turnSpeed = 360f;
                preferredDistance = 26f;
                gravityAcceleration = 6f;
                hitPoints = 1;
                bulletSpeed = 60f;
                fireCooldown = 3.4f;
                fireStartDelay = 3.25f;
                bulletSpreadAngle = 3f;
                bulletsPerShot = 1;
                multiShotSpacing = 10f;
                scoreValue = 100;
                transform.localScale = new Vector3(4f, 4f, 1f);
                TintSprite(Color.white);
                break;
        }

        nextFireTime = Time.time + fireStartDelay;
    }

    private void TintSprite(Color color)
    {
        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    private static EnemyVariant ChooseVariant()
    {
        var score = ScoreTracker.CurrentScore;
        int aceChance = score >= 2200 ? 35 : score >= 1200 ? 20 : 0;
        int spreadChance = score >= 300 ? 45 : 0;
        var roll = Random.Range(0, 100);

        // Should in theory make the higher level enemies spawn with higher score
        if (roll < aceChance)
            return EnemyVariant.Ace;

        if (roll < aceChance + spreadChance)
            return EnemyVariant.SpreadShooter;

        return EnemyVariant.Fighter;
    }

    private void FireBossPattern(Vector2 direction, Vector3 spawnPoint)
    {
        // Just makes ther boss fire in a pattern mimicking the other enemies
        switch (attackPatternIndex % 3)
        {
            case 0:
                FirePattern(direction, spawnPoint, 1, 0f, 140f, 1f, new Color(1f, 0.5f, 0.2f, 1f));
                break;

            case 1:
                FirePattern(direction, spawnPoint, 3, 14f, 110f, 1.15f, new Color(0.4f, 1f, 0.45f, 1f));
                break;

            default:
                FirePattern(direction, spawnPoint, 2, 6f, 170f, 0.9f, new Color(1f, 0.8f, 0.2f, 1f));
                break;
        }

        attackPatternIndex++;
    }

    private void FirePattern(Vector2 direction, Vector3 spawnPoint, int shotCount, float spacing, float speedOverride, float scaleMultiplier, Color bulletColor)
    {
        float centeredOffset = (shotCount - 1) * 0.5f;

        for (int i = 0; i < shotCount; i++)
        {
            var bullet = CreateBulletInstance(bulletColor, scaleMultiplier);
            if (bullet == null)
                continue;

            float patternOffset = (i - centeredOffset) * spacing;
            float spreadOffset = Random.Range(-bulletSpreadAngle, bulletSpreadAngle);
            Vector2 fireDirection = Quaternion.Euler(0f, 0f, patternOffset + spreadOffset) * direction;
            bullet.transform.position = spawnPoint;
            bullet.transform.rotation = Quaternion.FromToRotation(Vector3.up, fireDirection);
            bullet.Initialize(fireDirection, Vector2.zero, gameObject, speedOverride);
        }
    }

    private BulletPewPew CreateBulletInstance(Color overrideColor, float scaleMultiplier)
    {
        if (bulletPrefab != null)
        {
            var prefabBullet = Instantiate(bulletPrefab);
            prefabBullet.transform.localScale = new Vector3(0.95f, 1.7f, 1f) * scaleMultiplier;
            var renderer = prefabBullet.GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = overrideColor;
            return prefabBullet;
        }

        return BulletPewPew.CreateRuntimeBullet(
            "EnemyBullet",
            overrideColor,
            new Vector3(0.95f, 1.7f, 1f) * scaleMultiplier,
            9);
    }
}

public class EnemySpawnDirector : MonoBehaviour
{
    private static EnemySpawnDirector instance;

    [SerializeField] private float spawnCheckInterval = 0.45f;
    [SerializeField] private float respawnDelay = 0.2f;
    [SerializeField] private int baseTargetEnemies = 8;
    [SerializeField] private int extraEnemyPer800Score = 2;
    [SerializeField] private int maxTargetEnemies = 12;
    [SerializeField] private int bossTargetEnemies = 12;
    [SerializeField] private int bossScoreThreshold = 6000;

    private float nextSpawnCheckTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject directorObject = new GameObject("EnemySpawnDirector");
        instance = directorObject.AddComponent<EnemySpawnDirector>();
        DontDestroyOnLoad(directorObject);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        nextSpawnCheckTime = Time.time + respawnDelay;
    }

    private void Update()
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        if (Time.time < nextSpawnCheckTime)
            return;

        var player = FindFirstObjectByType<PlayerMovement>();
        if (player == null)
            return;

        if (!EnemyAI.BossSpawned && ScoreTracker.CurrentScore >= bossScoreThreshold)
        {
            EnemyAI.TrySpawnBoss(player.transform);
            nextSpawnCheckTime = Time.time + 0.5f;
        }

        var targetEnemies = EnemyAI.BossAlive
            ? bossTargetEnemies
            : Mathf.Min(
                maxTargetEnemies,
                baseTargetEnemies + (ScoreTracker.CurrentScore / 800) * extraEnemyPer800Score);

        while (EnemyAI.ActiveEnemyCount < targetEnemies && EnemyAI.TrySpawnEnemy(player.transform))
        {
            nextSpawnCheckTime = Time.time + respawnDelay;
        }

        if (EnemyAI.ActiveEnemyCount >= targetEnemies)
            nextSpawnCheckTime = Time.time + spawnCheckInterval;
    }
}
