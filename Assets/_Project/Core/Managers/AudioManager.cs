using UnityEngine;

namespace TillerQuest.Core
{
    /// <summary>
    /// Manages global audio playback for music and sound effects.
    /// Provides simple interface for playing audio across scenes.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("Audio Sources")]
        [SerializeField]
        private AudioSource musicSource;

        [SerializeField]
        private AudioSource sfxSource;

        [Header("Settings")]
        [SerializeField]
        private float masterVolume = 1f;

        [SerializeField]
        private float musicVolume = 0.7f;

        [SerializeField]
        private float sfxVolume = 1f;

        // Singleton
        private static AudioManager s_instance;
        public static AudioManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<AudioManager>();
                    if (s_instance == null)
                    {
                        GameObject go = new GameObject("[AudioManager]");
                        s_instance = go.AddComponent<AudioManager>();
                    }
                }
                return s_instance;
            }
        }

        // Properties
        public float MasterVolume
        {
            get => masterVolume;
            set
            {
                masterVolume = Mathf.Clamp01(value);
                UpdateVolumes();
            }
        }

        public float MusicVolume
        {
            get => musicVolume;
            set
            {
                musicVolume = Mathf.Clamp01(value);
                UpdateVolumes();
            }
        }

        public float SfxVolume
        {
            get => sfxVolume;
            set
            {
                sfxVolume = Mathf.Clamp01(value);
                UpdateVolumes();
            }
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
        }

        private void InitializeAudioSources()
        {
            // Create music source if needed
            if (musicSource == null)
            {
                GameObject musicGO = new GameObject("Music");
                musicGO.transform.SetParent(transform);
                musicSource = musicGO.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
            }

            // Create SFX source if needed
            if (sfxSource == null)
            {
                GameObject sfxGO = new GameObject("SFX");
                sfxGO.transform.SetParent(transform);
                sfxSource = sfxGO.AddComponent<AudioSource>();
                sfxSource.loop = false;
                sfxSource.playOnAwake = false;
            }

            UpdateVolumes();
            Debug.Log("[AudioManager] Initialized");
        }

        private void UpdateVolumes()
        {
            if (musicSource != null)
            {
                musicSource.volume = masterVolume * musicVolume;
            }

            if (sfxSource != null)
            {
                sfxSource.volume = masterVolume * sfxVolume;
            }

            AudioListener.volume = masterVolume;
        }

        /// <summary>
        /// Plays background music
        /// </summary>
        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioManager] Cannot play music: clip is null");
                return;
            }

            musicSource.clip = clip;
            musicSource.loop = loop;
            musicSource.Play();
            Debug.Log($"[AudioManager] Playing music: {clip.name}");
        }

        /// <summary>
        /// Stops background music
        /// </summary>
        public void StopMusic()
        {
            if (musicSource.isPlaying)
            {
                musicSource.Stop();
                Debug.Log("[AudioManager] Music stopped");
            }
        }

        /// <summary>
        /// Plays a sound effect
        /// </summary>
        public void PlaySFX(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioManager] Cannot play SFX: clip is null");
                return;
            }

            sfxSource.PlayOneShot(clip);
        }

        /// <summary>
        /// Plays a sound effect at a specific volume
        /// </summary>
        public void PlaySFX(AudioClip clip, float volumeScale)
        {
            if (clip == null)
            {
                Debug.LogWarning("[AudioManager] Cannot play SFX: clip is null");
                return;
            }

            sfxSource.PlayOneShot(clip, volumeScale);
        }

        /// <summary>
        /// Pauses the music
        /// </summary>
        public void PauseMusic()
        {
            if (musicSource.isPlaying)
            {
                musicSource.Pause();
            }
        }

        /// <summary>
        /// Resumes the music
        /// </summary>
        public void ResumeMusic()
        {
            if (!musicSource.isPlaying)
            {
                musicSource.UnPause();
            }
        }

        /// <summary>
        /// Mutes all audio
        /// </summary>
        public void MuteAll(bool mute)
        {
            AudioListener.pause = mute;
        }
    }
}
