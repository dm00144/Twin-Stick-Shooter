using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class BulletPewPew : MonoBehaviour
{
    [SerializeField] private float speed = 20f;
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float homingStrength;
    [SerializeField] private bool targetsEnemies;

    private static Sprite fallbackBulletSprite;
    private Rigidbody2D rb;
    private GameObject owner;
    private bool firedByPlayer;
    private Transform homingTarget;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        Collider2D bulletCollider = GetComponent<Collider2D>();
        bulletCollider.isTrigger = true;
    }

    private void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    public void Initialize(Vector2 direction, Vector2 inheritedVelocity, GameObject bulletOwner, float overrideSpeed = -1f)
    {
        owner = bulletOwner;
        firedByPlayer = bulletOwner != null && (bulletOwner.GetComponent<PlayerMovement>() != null || bulletOwner.GetComponent<AllyPlane>() != null);

        if (overrideSpeed > 0f)
            speed = overrideSpeed;

        rb.linearVelocity = direction.normalized * speed + inheritedVelocity;
    }

    public void SetLifeTime(float overrideLifeTime)
    {
        if (overrideLifeTime > 0f)
            lifeTime = overrideLifeTime;
    }

    public void ConfigureDamage(int newDamage)
    {
        damage = Mathf.Max(1, newDamage);
    }

    public void ConfigureHoming(float strength, bool seekEnemies)
    {
        homingStrength = Mathf.Max(0f, strength);
        targetsEnemies = seekEnemies;
    }

    public void Fire(Vector2 direction)
    {
        Initialize(direction, Vector2.zero, null);
    }

    private void FixedUpdate()
    {
        if (homingStrength <= 0f)
            return;

        if (homingTarget == null)
            homingTarget = FindHomingTarget();

        if (homingTarget == null)
            return;

        Vector2 desiredDirection = ((Vector2)homingTarget.position - rb.position).normalized;
        Vector2 currentDirection = rb.linearVelocity.sqrMagnitude > 0.01f
            ? rb.linearVelocity.normalized
            : (Vector2)transform.up;
        Vector2 steeredDirection = Vector2.Lerp(currentDirection, desiredDirection, homingStrength * Time.fixedDeltaTime).normalized;
        rb.linearVelocity = steeredDirection * speed;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, steeredDirection);
    }

    public static void DestroyAllActiveBullets()
    {
        BulletPewPew[] bullets = FindObjectsByType<BulletPewPew>(FindObjectsSortMode.None);
        for (int i = 0; i < bullets.Length; i++)
        {
            if (bullets[i] != null)
                Destroy(bullets[i].gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (owner != null && other.transform.root == owner.transform.root)
            return;

        PlayerMovement player = other.GetComponent<PlayerMovement>();
        if (player != null && !firedByPlayer)
        {
            player.TakeHit();
            Destroy(gameObject);
            return;
        }

        AllyPlane ally = other.GetComponent<AllyPlane>();
        if (ally != null && !firedByPlayer)
        {
            ally.TakeHit();
            Destroy(gameObject);
            return;
        }

        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy != null && firedByPlayer)
        {
            enemy.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        if (!other.isTrigger)
            Destroy(gameObject);
    }

    public static BulletPewPew CreateRuntimeBullet(string bulletName, Color color, Vector3 scale, int sortingOrder = 10)
    {
        GameObject bulletObject = new GameObject(bulletName);
        bulletObject.transform.localScale = scale;

        SpriteRenderer bulletRenderer = bulletObject.AddComponent<SpriteRenderer>();
        bulletRenderer.sprite = GetFallbackBulletSprite();
        bulletRenderer.color = color;
        bulletRenderer.sortingOrder = sortingOrder;

        Rigidbody2D bulletBody = bulletObject.AddComponent<Rigidbody2D>();
        bulletBody.gravityScale = 0f;
        bulletBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D bulletCollider = bulletObject.AddComponent<CircleCollider2D>();
        bulletCollider.isTrigger = true;
        bulletCollider.radius = 0.5f;

        return bulletObject.AddComponent<BulletPewPew>();
    }

    private Transform FindHomingTarget()
    {
        if (!targetsEnemies)
            return null;

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

        return bestEnemy != null ? bestEnemy.transform : null;
    }

    private static Sprite GetFallbackBulletSprite()
    {
        if (fallbackBulletSprite != null)
            return fallbackBulletSprite;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.name = "RuntimeBulletTexture";
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        fallbackBulletSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        fallbackBulletSprite.name = "RuntimeBulletSprite";

        return fallbackBulletSprite;
    }
}
