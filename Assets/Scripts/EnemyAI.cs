using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyAI : MonoBehaviour
{
    private enum EnemyVariant
    {
        Fighter,
        SpreadShooter,
        Ace,
        Boss,
        BossFighter,
        BossSpreadShooter,
        BossAce
    }

    private static EnemyAI spawnTemplate;
    private static int activeEnemyCount;
    private static int activeBossCount;
    private static bool isMakingTemplate;
    private static bool bossSpawned;

    [Header("Variant")]
    [SerializeField] private EnemyVariant variant = EnemyVariant.Fighter;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5.5f;
    [SerializeField] private float turnSpeed = 360f;
    [SerializeField] private float preferredDistance = 26f;
    [SerializeField] private float lateralSpeed = 5f;
    [SerializeField] private float radialCorrectionStrength = 0.35f;
    [SerializeField] private float strafeDirectionChangeInterval = 3.5f;
    [SerializeField] private float lateralWobbleStrength = 1.5f;
    [SerializeField] private float lateralWobbleFrequency = 1.3f;
    [SerializeField] private float gravityAcceleration = 6f;
    [SerializeField] private float separationRadius = 12f;
    [SerializeField] private float separationStrength = 10f;

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
    private int strafeDirection = 1;
    private float nextStrafeDirectionChangeTime;
    private float lateralWobbleOffset;
    private bool bossSpawnReported;
    private bool bossDefeatReported;

    public static int ActiveEnemyCount => activeEnemyCount;
    public static bool BossAlive => activeBossCount > 0;
    public static bool BossSpawned => bossSpawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        spawnTemplate = null;
        activeEnemyCount = 0;
        activeBossCount = 0;
        isMakingTemplate = false;
        bossSpawned = false;
        initialWaveSpawned = false;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;

        ApplyVariantSprite();

        // Each variant rewrites these inspector defaults. The enum is basically the enemy's job title.
        ApplyVariantPreset();
        ConfigureStrafeMovement();

        // One hidden template handles runtime spawning so we do not need hand-built prefabs yet.
        EnsureSpawnTemplateExists();

        if (!isMakingTemplate)
            activeEnemyCount++;

        if (IsBossVariant(variant) && !isMakingTemplate)
            RegisterBossSpawned();
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
            // When gameplay pauses, enemies coast down instead of freezing mid-lunge.
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

        float distanceError = distanceToPlayer - preferredDistance;
        Vector2 desiredVelocity;

        if (IsBossVariant(variant))
        {
            // Bosses are big blunt instruments. They chase range instead of doing the UFO slide.
            var moveDirection = Mathf.Abs(distanceError) < 3f ? 0f : Mathf.Sign(distanceError);
            desiredVelocity = aim * (moveSpeed * moveDirection);
        }
        else
        {
            MaybeChangeStrafeDirection();

            // Regular enemies slide around like smug little UFOs while correcting back to firing range.
            Vector2 lateralDirection = new Vector2(-aim.y, aim.x) * strafeDirection;
            float wobble = Mathf.Sin((Time.time + lateralWobbleOffset) * lateralWobbleFrequency) * lateralWobbleStrength;
            float radialSpeed = Mathf.Clamp(distanceError * radialCorrectionStrength, -moveSpeed, moveSpeed);
            desiredVelocity = lateralDirection * (lateralSpeed + wobble);
            desiredVelocity += aim * radialSpeed;
        }

        desiredVelocity += GetSeparationVelocity();
        rb.linearVelocity = Vector2.ClampMagnitude(desiredVelocity, moveSpeed);
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
            GameAudio.PlayEnemyDeath();
            // Bosses report into stage progression; regular enemies just cash out score and vanish.
            if (IsBossVariant(variant))
                ReportBossDefeated();
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (gameObject.scene.IsValid())
            activeEnemyCount = Mathf.Max(0, activeEnemyCount - 1);

        if (IsBossVariant(variant) && gameObject.scene.IsValid() && bossSpawnReported && !bossDefeatReported)
            activeBossCount = Mathf.Max(0, activeBossCount - 1);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        var playerMovement = collision.collider.GetComponent<PlayerMovement>();
        if (playerMovement != null)
            playerMovement.TakeHit();
        else
        {
            var ally = collision.collider.GetComponent<AllyPlane>();
            if (ally == null)
                return;

            ally.TakeHit();
        }

        if (!IsBossVariant(variant))
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        var ally = other.GetComponent<AllyPlane>();
        if (ally == null)
            return;

        ally.TakeHit();

        if (!IsBossVariant(variant))
            Destroy(gameObject);
    }

    private void SpawnInitialWaveIfNeeded()
    {
        if (skipInitialWaveSpawn || initialWaveSpawned || player == null) return;

        // The scene enemy becomes the seed for the first wave, then the template handles reinforcements.
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

        // Hide one cloned enemy offstage and use it as the factory for every runtime spawn.
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

        if (IsBossVariant(variant))
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

    public static void DestroyAllActiveEnemies()
    {
        EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null || enemies[i] == spawnTemplate)
                continue;

            Destroy(enemies[i].gameObject);
        }

        activeEnemyCount = 0;
        activeBossCount = 0;
        bossSpawned = false;
    }

    public static bool TrySpawnBoss(Transform playerTransform)
    {
        if (spawnTemplate == null || playerTransform == null || bossSpawned)
            return false;

        if (StageProgression.CurrentStage >= StageProgression.MaxStage)
        {
            // Final stage is the boss reunion tour. Rude, but dramatic.
            spawnTemplate.SpawnStageFourBosses(playerTransform);
            bossSpawned = true;
            return true;
        }

        Vector2 spawnPosition = spawnTemplate.GetSpawnPositionFor(playerTransform);
        spawnTemplate.SpawnEnemyAt(spawnPosition, GetBossVariantForStage(StageProgression.CurrentStage));
        bossSpawned = true;
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
        // Variant is assigned after Instantiate, so reapply art/stats or every clone acts like its parent.
        clone.ApplyVariantSprite();
        clone.ApplyVariantPreset();
        clone.ConfigureStrafeMovement();
        if (IsBossVariant(spawnVariant))
            clone.RegisterBossSpawned();
        clone.enabled = true;
        clone.gameObject.SetActive(true);
        return clone;
    }

    private Vector2 GetSpawnPositionFor(Transform playerTransform)
    {
        var boundsCollider = ResolveSpawnBounds();
        Vector2 fallback = playerTransform.position;

        // Spawn in a loose ring so enemies arrive from danger-space, not directly on the player's lap.
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

    private void SpawnStageFourBosses(Transform playerTransform)
    {
        // Three flavors of bad news, spaced out a bit so they do not spawn as one mega-lump.
        EnemyVariant[] finalBosses =
        {
            EnemyVariant.BossFighter,
            EnemyVariant.BossSpreadShooter,
            EnemyVariant.BossAce
        };

        for (int i = 0; i < finalBosses.Length; i++)
        {
            Vector2 spawnPosition = GetSpawnPositionFor(playerTransform);
            Vector3 offset = Quaternion.Euler(0f, 0f, i * 120f) * Vector3.up * 22f;
            spawnPosition += (Vector2)offset;
            SpawnEnemyAt(spawnPosition, finalBosses[i]);
        }
    }

    private void RegisterBossSpawned()
    {
        if (bossSpawnReported)
            return;

        // Track boss count, not just a bool, because stage four has multiple bosses alive together.
        bossSpawnReported = true;
        activeBossCount++;
        bossSpawned = true;
        bossDefeatReported = false;
    }

    private void ReportBossDefeated()
    {
        if (bossDefeatReported)
            return;

        bossDefeatReported = true;
        activeBossCount = Mathf.Max(0, activeBossCount - 1);
        // Only the last boss death gets to advance the stage. Teamwork, but evil.
        if (activeBossCount <= 0)
            StageProgression.HandleBossDestroyed();
    }

    private static bool IsBossVariant(EnemyVariant enemyVariant)
    {
        return enemyVariant == EnemyVariant.Boss
            || enemyVariant == EnemyVariant.BossFighter
            || enemyVariant == EnemyVariant.BossSpreadShooter
            || enemyVariant == EnemyVariant.BossAce;
    }

    private static EnemyVariant GetBossVariantForStage(int stage)
    {
        return stage switch
        {
            1 => EnemyVariant.BossFighter,
            2 => EnemyVariant.BossSpreadShooter,
            3 => EnemyVariant.BossAce,
            _ => EnemyVariant.Boss
        };
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
                lateralSpeed = 3.2f;
                radialCorrectionStrength = 0.22f;
                strafeDirectionChangeInterval = 8f;
                lateralWobbleStrength = 0.6f;
                lateralWobbleFrequency = 0.6f;
                gravityAcceleration = 5f;
                separationRadius = 16f;
                separationStrength = 12f;
                hitPoints = 2;
                bulletSpeed = 52f;
                fireCooldown = 4.2f;
                fireStartDelay = 3.5f;
                bulletSpreadAngle = 2f;
                bulletsPerShot = 3;
                multiShotSpacing = 12f;
                scoreValue = 200;
                transform.localScale = new Vector3(4.8f, 4.8f, 1f);
                TintSprite(Color.white);
                break;

            case EnemyVariant.Ace:
                moveSpeed = 35f;
                turnSpeed = 640f;
                preferredDistance = 5f;
                lateralSpeed = 28f;
                radialCorrectionStrength = 1.7f;
                strafeDirectionChangeInterval = 3.2f;
                lateralWobbleStrength = 2.5f;
                lateralWobbleFrequency = 1.4f;
                gravityAcceleration = 7f;
                separationRadius = 24f;
                separationStrength = 32f;
                hitPoints = 4;
                bulletSpeed = 90f;
                fireCooldown = .3f;
                fireStartDelay = 2f;
                bulletSpreadAngle = 6f;
                bulletsPerShot = 1;
                multiShotSpacing = 0f;
                scoreValue = 350;
                transform.localScale = new Vector3(4.25f, 4.25f, 1f);
                TintSprite(Color.white);
                break;

            case EnemyVariant.BossFighter:
                moveSpeed = 30f;
                turnSpeed = 180f;
                preferredDistance = 20f;
                lateralSpeed = 18f;
                radialCorrectionStrength = 0.8f;
                strafeDirectionChangeInterval = 5f;
                lateralWobbleStrength = 0f;
                lateralWobbleFrequency = 0f;
                gravityAcceleration = 4f;
                separationRadius = 30f;
                separationStrength = 18f;
                hitPoints = 60;
                bulletSpeed = 180f;
                fireCooldown = 0.35f;
                fireStartDelay = 1.5f;
                bulletSpreadAngle = 2f;
                bulletsPerShot = 1;
                multiShotSpacing = 2f;
                scoreValue = 2000;
                transform.localScale = new Vector3(15f, 24f, 1f);
                TintSprite(new Color(1f, 0.96f, 0.9f, 1f));
                break;

            case EnemyVariant.BossSpreadShooter:
                moveSpeed = 22f;
                turnSpeed = 140f;
                preferredDistance = 42f;
                lateralSpeed = 14f;
                radialCorrectionStrength = 0.45f;
                strafeDirectionChangeInterval = 6f;
                lateralWobbleStrength = 0.8f;
                lateralWobbleFrequency = 0.6f;
                gravityAcceleration = 4.5f;
                separationRadius = 36f;
                separationStrength = 22f;
                hitPoints = 74;
                bulletSpeed = 125f;
                fireCooldown = 0.68f;
                fireStartDelay = 1.3f;
                bulletSpreadAngle = 4f;
                bulletsPerShot = 5;
                multiShotSpacing = 12f;
                scoreValue = 2400;
                transform.localScale = new Vector3(17f, 17f, 1f);
                TintSprite(new Color(0.7f, 1f, 0.82f, 1f));
                break;

            case EnemyVariant.BossAce:
                moveSpeed = 52f;
                turnSpeed = 520f;
                preferredDistance = 12f;
                lateralSpeed = 38f;
                radialCorrectionStrength = 1.25f;
                strafeDirectionChangeInterval = 2.4f;
                lateralWobbleStrength = 2.2f;
                lateralWobbleFrequency = 1.4f;
                gravityAcceleration = 6f;
                separationRadius = 34f;
                separationStrength = 36f;
                hitPoints = 54;
                bulletSpeed = 170f;
                fireCooldown = 0.22f;
                fireStartDelay = 1f;
                bulletSpreadAngle = 7f;
                bulletsPerShot = 1;
                multiShotSpacing = 0f;
                scoreValue = 2800;
                transform.localScale = new Vector3(13f, 13f, 1f);
                TintSprite(new Color(1f, 0.78f, 1f, 1f));
                break;

            case EnemyVariant.Boss:
                moveSpeed = 28f;
                turnSpeed = 180f;
                preferredDistance = 24f;
                lateralSpeed = 16f;
                radialCorrectionStrength = 0.7f;
                strafeDirectionChangeInterval = 5f;
                lateralWobbleStrength = 0f;
                lateralWobbleFrequency = 0f;
                gravityAcceleration = 4f;
                separationRadius = 30f;
                separationStrength = 18f;
                hitPoints = 70;
                bulletSpeed = 150f;
                fireCooldown = 0.4f;
                fireStartDelay = 1.5f;
                bulletSpreadAngle = 2f;
                bulletsPerShot = 2;
                multiShotSpacing = 8f;
                scoreValue = 2000;
                transform.localScale = new Vector3(15f, 20f, 1f);
                TintSprite(Color.white);
                break;

            default:
                moveSpeed = 5.5f;
                turnSpeed = 360f;
                preferredDistance = 26f;
                lateralSpeed = 4.8f;
                radialCorrectionStrength = 0.32f;
                strafeDirectionChangeInterval = 7f;
                lateralWobbleStrength = 0.8f;
                lateralWobbleFrequency = 0.75f;
                gravityAcceleration = 6f;
                separationRadius = 12f;
                separationStrength = 10f;
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

    private void ConfigureStrafeMovement()
    {
        // Randomize the wiggle so spawned enemies do not fly like synchronized swimmers.
        strafeDirection = Random.value < 0.5f ? -1 : 1;
        lateralWobbleOffset = Random.Range(0f, Mathf.PI * 2f);
        nextStrafeDirectionChangeTime = Time.time + Random.Range(
            strafeDirectionChangeInterval * 0.65f,
            strafeDirectionChangeInterval * 1.35f);
    }

    private void MaybeChangeStrafeDirection()
    {
        if (strafeDirectionChangeInterval <= 0f || Time.time < nextStrafeDirectionChangeTime)
            return;

        strafeDirection *= -1;
        nextStrafeDirectionChangeTime = Time.time + Random.Range(
            strafeDirectionChangeInterval * 0.75f,
            strafeDirectionChangeInterval * 1.35f);
    }

    private Vector2 GetSeparationVelocity()
    {
        if (separationRadius <= 0f || separationStrength <= 0f)
            return Vector2.zero;

        // Basic crowd control: enemies nudge away from each other instead of stacking into one blob.
        Vector2 separation = Vector2.zero;
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, separationRadius);

        for (int i = 0; i < nearbyColliders.Length; i++)
        {
            EnemyAI otherEnemy = nearbyColliders[i].GetComponent<EnemyAI>();
            if (otherEnemy == null || otherEnemy == this || otherEnemy.isDead)
                continue;

            Vector2 awayFromOther = (Vector2)(transform.position - otherEnemy.transform.position);
            float distance = awayFromOther.magnitude;
            if (distance <= 0.01f)
                continue;

            float closeness = 1f - Mathf.Clamp01(distance / separationRadius);
            separation += awayFromOther.normalized * closeness;
        }

        return separation * separationStrength;
    }

    private void TintSprite(Color color)
    {
        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    private void ApplyVariantSprite()
    {
        if (spriteRenderer == null)
            return;

        Sprite variantSprite = variant switch
        {
            EnemyVariant.SpreadShooter => GameSpriteLibrary.GetEnemySpreadShooterSprite(),
            EnemyVariant.Ace => GameSpriteLibrary.GetEnemyAceSprite(),
            EnemyVariant.BossFighter => GameSpriteLibrary.GetEnemyBossSprite(),
            EnemyVariant.BossSpreadShooter => GameSpriteLibrary.GetEnemySpreadShooterSprite(),
            EnemyVariant.BossAce => GameSpriteLibrary.GetEnemyAceSprite(),
            EnemyVariant.Boss => GameSpriteLibrary.GetEnemyBossSprite(),
            _ => GameSpriteLibrary.GetEnemyFighterSprite()
        };

        if (variantSprite == null)
            return;

        spriteRenderer.sprite = variantSprite;
        spriteRenderer.color = Color.white;
        AlignColliderToSprite(variantSprite);
    }

    private void AlignColliderToSprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        CircleCollider2D circleCollider = GetComponent<CircleCollider2D>();
        if (circleCollider != null)
        {
            float baseSize = Mathf.Min(sprite.bounds.size.x, sprite.bounds.size.y);
            circleCollider.radius = baseSize * 0.28f;
            circleCollider.offset = Vector2.zero;
            return;
        }

        BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
            return;

        Vector2 spriteSize = sprite.bounds.size;
        if (IsBossVariant(variant))
        {
            boxCollider.size = new Vector2(spriteSize.x * 0.62f, spriteSize.y * 0.82f);
            boxCollider.offset = new Vector2(0f, spriteSize.y * 0.03f);
        }
        else
        {
            boxCollider.size = new Vector2(spriteSize.x * 0.62f, spriteSize.y * 0.62f);
            boxCollider.offset = Vector2.zero;
        }
    }

    private static EnemyVariant ChooseVariant()
    {
        var score = ScoreTracker.CurrentScore;
        int aceChance = score >= 2200 ? 35 : score >= 1200 ? 20 : 0;
        int spreadChance = score >= 300 ? 45 : 0;
        var roll = Random.Range(0, 100);

        // Higher score means command is done being polite and starts sending nastier ships.
        if (roll < aceChance)
            return EnemyVariant.Ace;

        if (roll < aceChance + spreadChance)
            return EnemyVariant.SpreadShooter;

        return EnemyVariant.Fighter;
    }

    private void FireBossPattern(Vector2 direction, Vector3 spawnPoint)
    {
        if (variant == EnemyVariant.BossSpreadShooter)
        {
            // Spread boss owns the screen by volume. Subtlety was not in the briefing.
            FirePattern(direction, spawnPoint, 5, 13f, bulletSpeed, 1.05f, new Color(0.4f, 1f, 0.55f, 1f));
            attackPatternIndex++;
            return;
        }

        if (variant == EnemyVariant.BossAce)
        {
            // Ace boss fires fewer shots, but fast enough to make dodging feel personal.
            int shotCount = attackPatternIndex % 4 == 0 ? 3 : 1;
            float spacing = shotCount > 1 ? 8f : 0f;
            FirePattern(direction, spawnPoint, shotCount, spacing, bulletSpeed, 0.85f, new Color(1f, 0.45f, 1f, 1f));
            attackPatternIndex++;
            return;
        }

        if (variant == EnemyVariant.BossFighter)
        {
            // Fighter boss is the classic bruiser: direct, chunky, and very committed.
            int shotCount = attackPatternIndex % 3 == 0 ? 2 : 1;
            FirePattern(direction, spawnPoint, shotCount, 9f, bulletSpeed, 1.1f, new Color(1f, 0.5f, 0.2f, 1f));
            attackPatternIndex++;
            return;
        }

        // Fallback boss pattern, mostly here for old scenes or future boss experiments.
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

        if (!EnemyAI.BossSpawned && StageProgression.CurrentStageScore >= bossScoreThreshold)
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
