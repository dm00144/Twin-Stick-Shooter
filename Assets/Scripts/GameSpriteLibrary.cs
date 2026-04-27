using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class GameSpriteLibrary
{
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

    public static Sprite GetPlayerShipSprite() => LoadSprite("Sprites/PlayerShip");
    public static Sprite GetSquadronShipSprite() => LoadSprite("Sprites/SquadronShip");
    public static Sprite GetEnemyFighterSprite() => LoadSprite("Sprites/EnemyFighter");
    public static Sprite GetEnemySpreadShooterSprite() => LoadSprite("Sprites/EnemySpreadShooter");
    public static Sprite GetEnemyAceSprite() => LoadSprite("Sprites/EnemyAce");
    public static Sprite GetEnemyBossSprite() => LoadSprite("Sprites/EnemyBoss");
    public static Sprite GetCloudSprite() => LoadSprite("Sprites/Cloud");

    public static Sprite GetStageBackgroundSprite(int stage)
    {
        return stage switch
        {
            2 => LoadSprite("Backgrounds/PlayableAreaSkyPrototype"),
            3 => LoadSprite("Backgrounds/PlayableAreaEvening"),
            4 => LoadSprite("Backgrounds/PlayableAreaNight"),
            _ => LoadSprite("Backgrounds/PlayableAreaMorning")
        };
    }

    private static Sprite LoadSprite(string resourcePath)
    {
        if (SpriteCache.TryGetValue(resourcePath, out Sprite cachedSprite))
            return cachedSprite;

        Texture2D texture = LoadProcessedTexture(resourcePath);
        if (texture == null)
            return null;

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;

        float pixelsPerUnit = Mathf.Max(1f, Mathf.Min(texture.width, texture.height) / 2.5f);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit,
            0u,
            SpriteMeshType.FullRect);

        sprite.name = Path.GetFileName(resourcePath);
        SpriteCache[resourcePath] = sprite;
        return sprite;
    }

    private static Texture2D LoadProcessedTexture(string resourcePath)
    {
        string filePath = Path.Combine(Application.dataPath, "Resources", resourcePath + ".png");
        if (File.Exists(filePath))
        {
            byte[] imageBytes = File.ReadAllBytes(filePath);
            Texture2D sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            sourceTexture.LoadImage(imageBytes, false);
            sourceTexture.filterMode = FilterMode.Point;
            sourceTexture.wrapMode = TextureWrapMode.Clamp;
            return StripBackgroundAndCrop(sourceTexture);
        }

        Texture2D fallbackTexture = Resources.Load<Texture2D>(resourcePath);
        if (fallbackTexture == null)
            return null;

        return fallbackTexture;
    }

    private static Texture2D StripBackgroundAndCrop(Texture2D sourceTexture)
    {
        int width = sourceTexture.width;
        int height = sourceTexture.height;
        Color32[] pixels = sourceTexture.GetPixels32();
        bool[] backgroundMask = new bool[pixels.Length];
        Queue<int> queue = new Queue<int>();

        EnqueueBorderPixels(width, height, pixels, backgroundMask, queue);

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            TryEnqueueBackgroundPixel(x - 1, y, width, height, pixels, backgroundMask, queue);
            TryEnqueueBackgroundPixel(x + 1, y, width, height, pixels, backgroundMask, queue);
            TryEnqueueBackgroundPixel(x, y - 1, width, height, pixels, backgroundMask, queue);
            TryEnqueueBackgroundPixel(x, y + 1, width, height, pixels, backgroundMask, queue);
        }

        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int i = 0; i < pixels.Length; i++)
        {
            if (backgroundMask[i])
                pixels[i].a = 0;

            if (pixels[i].a <= 0)
                continue;

            int x = i % width;
            int y = i / width;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        if (maxX < minX || maxY < minY)
            return sourceTexture;

        int padding = 2;
        minX = Mathf.Max(0, minX - padding);
        minY = Mathf.Max(0, minY - padding);
        maxX = Mathf.Min(width - 1, maxX + padding);
        maxY = Mathf.Min(height - 1, maxY + padding);

        int croppedWidth = maxX - minX + 1;
        int croppedHeight = maxY - minY + 1;
        Color32[] croppedPixels = new Color32[croppedWidth * croppedHeight];

        for (int y = 0; y < croppedHeight; y++)
        {
            for (int x = 0; x < croppedWidth; x++)
            {
                croppedPixels[y * croppedWidth + x] = pixels[(minY + y) * width + (minX + x)];
            }
        }

        Texture2D croppedTexture = new Texture2D(croppedWidth, croppedHeight, TextureFormat.RGBA32, false);
        croppedTexture.SetPixels32(croppedPixels);
        croppedTexture.Apply(false, false);
        croppedTexture.filterMode = FilterMode.Point;
        croppedTexture.wrapMode = TextureWrapMode.Clamp;
        return croppedTexture;
    }

    private static void EnqueueBorderPixels(int width, int height, Color32[] pixels, bool[] backgroundMask, Queue<int> queue)
    {
        for (int x = 0; x < width; x++)
        {
            TryEnqueueBackgroundPixel(x, 0, width, height, pixels, backgroundMask, queue);
            TryEnqueueBackgroundPixel(x, height - 1, width, height, pixels, backgroundMask, queue);
        }

        for (int y = 1; y < height - 1; y++)
        {
            TryEnqueueBackgroundPixel(0, y, width, height, pixels, backgroundMask, queue);
            TryEnqueueBackgroundPixel(width - 1, y, width, height, pixels, backgroundMask, queue);
        }
    }

    private static void TryEnqueueBackgroundPixel(
        int x,
        int y,
        int width,
        int height,
        Color32[] pixels,
        bool[] backgroundMask,
        Queue<int> queue)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return;

        int index = y * width + x;
        if (backgroundMask[index] || !IsLikelyBackground(pixels[index]))
            return;

        backgroundMask[index] = true;
        queue.Enqueue(index);
    }

    private static bool IsLikelyBackground(Color32 pixel)
    {
        int max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
        int min = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
        int brightness = (pixel.r + pixel.g + pixel.b) / 3;
        return pixel.a > 0 && brightness >= 218 && (max - min) <= 24;
    }
}
