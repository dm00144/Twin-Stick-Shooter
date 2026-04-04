using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CircleCollider2D))]
public class CloudHazard : MonoBehaviour
{
    private static Sprite generatedCloudSprite;
    private const float VisualScaleMultiplier = 4f;

    [SerializeField] private float slowMultiplier = 0.4f;
    [SerializeField] private float triggerRadius = 20f;
    [SerializeField] private int sortingOrder = 5;
    [SerializeField] private float visualZOffset = -0.5f;

    private readonly HashSet<PlayerMovement> affectedPlayers = new HashSet<PlayerMovement>();
    private Sprite cloudSprite;

    public void Initialize(Sprite sprite, float movementSlowMultiplier, float radius)
    {
        cloudSprite = sprite != null ? sprite : GetOrCreateCloudSprite();
        slowMultiplier = movementSlowMultiplier;
        triggerRadius = radius;
        SyncTrigger();
        if (transform.childCount == 0)
            BuildVisual();
    }

    private void Awake()
    {
        SyncTrigger();
        if (transform.childCount == 0)
            BuildVisual();
    }

    private void OnDisable()
    {
       
        foreach (var player in affectedPlayers)
        {
            if (player != null)
                player.ClearMovementModifier(GetInstanceID());
        }

        affectedPlayers.Clear();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerMovement player))
            return;

        player.SetMovementModifier(GetInstanceID(), slowMultiplier);
        affectedPlayers.Add(player);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.TryGetComponent(out PlayerMovement player))
            return;

        player.ClearMovementModifier(GetInstanceID());
        affectedPlayers.Remove(player);
    }

    private void SyncTrigger()
    {
        var trigger = GetComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = triggerRadius;
    }

    private void BuildVisual()
    {
        cloudSprite ??= GetOrCreateCloudSprite();

        if (cloudSprite == null)
            return;

        // A really scuffed way of making a cloud because i couldnt get random prefabs to work
        var offsets = new[]
        {
            new Vector2(-3.6f, -1.2f),
            new Vector2(0f, -1.55f),
            new Vector2(3.6f, -1.2f),
            new Vector2(-1.9f, 1.85f),
            new Vector2(1.9f, 1.85f)
        };

        float[] sizes = { 5f, 5.6f, 5f, 4.7f, 4.7f };

        for (int i = 0; i < offsets.Length; i++)
        {
            var puff = new GameObject($"Puff_{i + 1}");
            puff.transform.SetParent(transform, false);
            var scaledOffset = offsets[i] * VisualScaleMultiplier;
            puff.transform.localPosition = new Vector3(scaledOffset.x, scaledOffset.y, visualZOffset);
            puff.transform.localScale = Vector3.one * (sizes[i] * VisualScaleMultiplier);

            var renderer = puff.AddComponent<SpriteRenderer>();
            renderer.sprite = cloudSprite;
            renderer.color = new Color(0.92f, 0.97f, 1f, 0.68f);
            renderer.sortingOrder = sortingOrder;
        }
    }

    private static Sprite GetOrCreateCloudSprite()
    {
        if (generatedCloudSprite != null)
            return generatedCloudSprite;

        // Cloud code
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeCloudCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.46f;
        var pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(radius - 3f, radius, distance));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        generatedCloudSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        return generatedCloudSprite;
    }
}

public class CloudFieldDirector : MonoBehaviour
{
    private static CloudFieldDirector instance;

    [SerializeField] private int cloudCount = 14;
    [SerializeField] private float cloudSlowMultiplier = 0.4f;
    [SerializeField] private float cloudRadius = 20f;
    [SerializeField] private float edgePadding = 14f;
    [SerializeField] private float playerClearRadius = 28f;
    [SerializeField] private int placementAttemptsPerCloud = 40;
    [SerializeField] private float minCloudSeparation = 42f;

    private readonly List<GameObject> spawnedClouds = new List<GameObject>();
    private readonly List<Vector2> cloudSpots = new List<Vector2>();
    private bool hasSpawnedClouds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject directorObject = new GameObject("CloudFieldDirector");
        instance = directorObject.AddComponent<CloudFieldDirector>();
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
        DontDestroyOnLoad(gameObject);
        GameFlowManager.StateChanged += HandleGameStateChanged;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        GameFlowManager.StateChanged -= HandleGameStateChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        HandleGameStateChanged();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        hasSpawnedClouds = false;
        ClearClouds();
        HandleGameStateChanged();
    }

    private void HandleGameStateChanged()
    {
        if (!GameFlowManager.IsGameplayActive)
        {
            hasSpawnedClouds = false;
            ClearClouds();
            return;
        }

        if (hasSpawnedClouds)
            return;

        SpawnClouds();
    }

    private void SpawnClouds()
    {
        ClearClouds();

        var player = FindFirstObjectByType<PlayerMovement>();
        if (player == null)
            return;

        BoxCollider2D boundsCollider = ResolveBoundsCollider();
        if (boundsCollider == null)
            return;

        Bounds bounds = boundsCollider.bounds;
        float minX = bounds.min.x + edgePadding;
        float maxX = bounds.max.x - edgePadding;
        float minY = bounds.min.y + edgePadding;
        float maxY = bounds.max.y - edgePadding;

        if (minX >= maxX || minY >= maxY)
            return;

        // a really rough attempt to randomize cloud gen
        for (int i = 0; i < cloudCount; i++)
        {
            var spawnPosition = PickSpot(player.transform.position, minX, maxX, minY, maxY);
            GameObject cloudObject = new GameObject($"CloudHazard_{i + 1}");
            cloudObject.transform.position = spawnPosition;

            var cloud = cloudObject.AddComponent<CloudHazard>();
            cloud.Initialize(null, cloudSlowMultiplier, cloudRadius);
            spawnedClouds.Add(cloudObject);
            cloudSpots.Add(spawnPosition);
        }

        hasSpawnedClouds = true;
    }

    private Vector2 PickSpot(Vector3 playerPosition, float minX, float maxX, float minY, float maxY)
    {
        var fallback = new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));

        for (int attempt = 0; attempt < placementAttemptsPerCloud; attempt++)
        {
            var candidate = new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));
            // makes sure that the cloud doesnt spawn ontop of the player
            if (Vector2.Distance(candidate, playerPosition) >= playerClearRadius && HasEnoughBreathingRoom(candidate))
                return candidate;
        }

        // If we get here the map is crowded so just make somthing workable
        return fallback;
    }

    private void ClearClouds()
    {
        foreach (var cloud in spawnedClouds)
        {
            if (cloud != null)
                Destroy(cloud);
        }

        spawnedClouds.Clear();
        cloudSpots.Clear();
    }

    private bool HasEnoughBreathingRoom(Vector2 candidate)
    {
        for (int i = 0; i < cloudSpots.Count; i++)
        {
            if (Vector2.Distance(candidate, cloudSpots[i]) < minCloudSeparation)
                return false;
        }

        return true;
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
