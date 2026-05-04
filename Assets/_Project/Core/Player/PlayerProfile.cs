using System;
using UnityEngine;

namespace TillerQuest.Player
{
    /// <summary>
    /// Represents a player's profile information.
    /// This data is loaded from the backend API after successful authentication.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        [Tooltip("Unique player identifier")]
        public string userId;

        [Tooltip("Player's name")]
        public string name;

        [Tooltip("Player's display name")]
        public string username;

        [Tooltip("Player's role or account type (e.g., 'USER', 'ADMIN')")]
        public string role;

        [Tooltip("URL to player's avatar image")]
        public string imageUrl;

        [Tooltip("A list of enumerable access rights or permissions")]
        public string[] access;

        [Tooltip("Player's current arena tokens")]
        public int arenaTokens;

        [Tooltip("Player level (0-100)")]
        public int level;

        [Tooltip("Total experience points")]
        public int xp;

        [Tooltip("Player's current dice colorset")]
        public string diceColorset;

        [Tooltip("Player's total gold")]
        public int gold;

        [Tooltip("Player preferences and settings")]
        public PlayerPreferences preferences;
    }

    /// <summary>
    /// Player preferences and settings
    /// </summary>
    [Serializable]
    public class PlayerPreferences
    {
        [Tooltip("Master audio volume (0-1)")]
        public float audioVolume = 1f;

        [Tooltip("Music volume (0-1)")]
        public float musicVolume = 0.7f;

        [Tooltip("SFX volume (0-1)")]
        public float sfxVolume = 1f;

        [Tooltip("Graphics quality setting (0=Low, 1=Medium, 2=High)")]
        public int graphicsQuality = 1;

        [Tooltip("Enable fullscreen mode")]
        public bool fullscreen = true;

        [Tooltip("Target frame rate (-1 = unlimited)")]
        public int targetFrameRate = 60;

        [Tooltip("Show tutorial hints")]
        public bool showTutorials = true;

        [Tooltip("Enable vibration/haptics")]
        public bool enableHaptics = true;

        /// <summary>
        /// Applies preferences to Unity settings
        /// </summary>
        public void Apply()
        {
            // Audio
            AudioListener.volume = audioVolume;

            // Graphics
            QualitySettings.SetQualityLevel(graphicsQuality);
            Screen.fullScreen = fullscreen;
            Application.targetFrameRate = targetFrameRate;

            Debug.Log(
                $"[PlayerPreferences] Applied settings: Quality={graphicsQuality}, FPS={targetFrameRate}"
            );
        }
    }
}
