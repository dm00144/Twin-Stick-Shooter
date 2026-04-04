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

    [Header("Health")]
    [SerializeField] private float maxHealth = 3f;
    [SerializeField] private float regenDelay = 2.5f;
    [SerializeField] private float regenRate = 1f;
    [SerializeField] private float contactInvulnerability = 1f;

    private Rigidbody2D rb;
    private float turnInput;
    private float thrustInput;
    private float currentHealth;
    private float nextFireTime;
    private float lastHitTime = float.NegativeInfinity;
    private float lastCombatActionTime = float.NegativeInfinity;
    private readonly Dictionary<int, float> movementModifiers = new Dictionary<int, float>();

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthNormalized => maxHealth <= 0f ? 0f : currentHealth / maxHealth;
    public bool IsDead => currentHealth <= 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        currentHealth = maxHealth;

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.gravityScale = 0f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void Update()
    {
        if (!GameFlowManager.IsGameplayActive)
            return;

        turnInput = 0f;
        thrustInput = 0f;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed)
                turnInput = 1f;
            else if (keyboard.dKey.isPressed)
                turnInput = -1f;

            if (keyboard.wKey.isPressed)
                thrustInput = 1f;

            if (keyboard.spaceKey.isPressed)
                TryShoot();
        }

        // Makes sure that shooting anbd damage interupts health regen
        bool canHeal = currentHealth < maxHealth && Time.time >= lastCombatActionTime + regenDelay;
        if (canHeal)
        {
            currentHealth = Mathf.Min(maxHealth, currentHealth + regenRate * Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (!GameFlowManager.IsGameplayActive)
        {
            // Let the ship coast down 
            rb.linearVelocity *= 0.98f;
            rb.angularVelocity *= 0.98f;
            return;
        }

        var slow = GetMovementMultiplier();

        rb.MoveRotation(rb.rotation + turnInput * rotationSpeed * Time.fixedDeltaTime);

        if (thrustInput > 0f)
            rb.linearVelocity += (Vector2)transform.up * (acceleration * slow * Time.fixedDeltaTime);
        else
            rb.linearVelocity *= idleDamping;

        // Clouds now input drag on player with regular fall speed
        rb.linearVelocity += Vector2.down * gravityAcceleration * slow * Time.fixedDeltaTime;

        var speedCap = maxSpeed * slow;
        if (rb.linearVelocity.magnitude <= speedCap) return;

        rb.linearVelocity = rb.linearVelocity.normalized * speedCap;
    }

    public void SetMovementModifier(int sourceId, float multiplier)
    {
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

        lastHitTime = Time.time;
        lastCombatActionTime = Time.time;
        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (currentHealth <= 0f)
            HandleDeath();
    }

    private void TryShoot()
    {
        if (Time.time < nextFireTime)
            return;

        var bullet = CreateBulletInstance();
        if (bullet == null)
            return;

        float spreadOffset = Random.Range(-bulletSpreadAngle, bulletSpreadAngle);
        Vector2 fireDirection = Quaternion.Euler(0f, 0f, spreadOffset) * transform.up;
        Vector3 spawnPoint = firePoint != null
            ? firePoint.position
            : transform.position + transform.up * muzzleDistance;

        bullet.transform.position = spawnPoint;
        bullet.transform.rotation = transform.rotation;
        bullet.SetLifeTime(bulletLifeTime);
        bullet.Initialize(fireDirection, Vector2.zero, gameObject, bulletSpeed);

        // Recoil inspired from Luftrausers to get a similar feel
        rb.AddForce(-fireDirection * recoilForce, ForceMode2D.Impulse);
        nextFireTime = Time.time + fireCooldown;
        lastCombatActionTime = Time.time;
    }

    private void HandleDeath()
    {
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        GameFlowManager.TriggerGameOver();
    }

    private float GetMovementMultiplier()
    {
        float multiplier = 1f;
        foreach (var value in movementModifiers.Values)
            multiplier = Mathf.Min(multiplier, value);

        return multiplier;
    }

    private BulletPewPew CreateBulletInstance()
    {
        if (bulletPrefab != null)
        {
            var bullet = Instantiate(bulletPrefab);
            bullet.transform.localScale = new Vector3(1.1f, 2f, 1f);
            return bullet;
        }

        return BulletPewPew.CreateRuntimeBullet(
            "PlayerBullet",
            new Color(1f, 0.92f, 0.3f, 1f),
            new Vector3(1.1f, 2f, 1f));
    }
}
