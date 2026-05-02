using UnityEngine;

public class GameAudio : MonoBehaviour
{
    private static GameAudio instance;

    private AudioSource musicSource;
    private AudioSource effectsSource;
    private AudioClip playerGunClip;
    private AudioClip squadronGunClip;
    private AudioClip enemyDeathClip;
    private AudioClip backgroundMusicClip;
    private float nextPlayerShotSoundTime;
    private float nextSquadronShotSoundTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        // Tiny always-on audio booth. Scenes can reload, but the mixer keeps its chair.
        GameObject audioObject = new GameObject("GameAudio");
        instance = audioObject.AddComponent<GameAudio>();
        DontDestroyOnLoad(audioObject);
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

        // SFX and music get separate sources so a laser pew never stomps the backing track.
        effectsSource = gameObject.AddComponent<AudioSource>();
        effectsSource.playOnAwake = false;
        effectsSource.spatialBlend = 0f;
        effectsSource.volume = 0.65f;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.volume = 0.22f;

        playerGunClip = Resources.Load<AudioClip>("Audio/player_gun");
        squadronGunClip = Resources.Load<AudioClip>("Audio/squadron_gun");
        enemyDeathClip = Resources.Load<AudioClip>("Audio/enemy_die");
        backgroundMusicClip = Resources.Load<AudioClip>("Audio/background_loop");
    }

    private void Update()
    {
        // Music only plays while the player is actually flying, not while reading story cards.
        if (GameFlowManager.IsGameplayActive)
            StartMusic();
        else
            StopMusic();
    }

    public static void PlayPlayerGun()
    {
        if (instance == null || Time.time < instance.nextPlayerShotSoundTime)
            return;

        // The player can fire very fast, so throttle the sound a bit before it becomes audio soup.
        instance.nextPlayerShotSoundTime = Time.time + 0.035f;
        instance.PlayEffect(instance.playerGunClip, 0.45f, Random.Range(0.95f, 1.08f));
    }

    public static void PlaySquadronGun()
    {
        if (instance == null || Time.time < instance.nextSquadronShotSoundTime)
            return;

        // Allies shoot in pairs a lot; this keeps the backup singers from clipping the mix.
        instance.nextSquadronShotSoundTime = Time.time + 0.06f;
        instance.PlayEffect(instance.squadronGunClip, 0.32f, Random.Range(1.02f, 1.16f));
    }

    public static void PlayEnemyDeath()
    {
        if (instance == null)
            return;

        instance.PlayEffect(instance.enemyDeathClip, 0.55f, Random.Range(0.88f, 1.12f));
    }

    private void StartMusic()
    {
        if (musicSource == null || musicSource.isPlaying || backgroundMusicClip == null)
            return;

        musicSource.clip = backgroundMusicClip;
        musicSource.Play();
    }

    private void StopMusic()
    {
        if (musicSource == null || !musicSource.isPlaying)
            return;

        musicSource.Stop();
    }

    private void PlayEffect(AudioClip clip, float volumeScale, float pitch)
    {
        if (effectsSource == null || clip == null)
            return;

        // A little pitch wiggle goes a long way when the same placeholder sound repeats.
        effectsSource.pitch = pitch;
        effectsSource.PlayOneShot(clip, volumeScale);
    }
}
