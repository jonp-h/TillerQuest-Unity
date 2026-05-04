using System;
using System.Threading.Tasks;
using TillerQuest.Auth;
using UnityEngine;

namespace TillerQuest.Player
{
    [Serializable]
    public class PlayerProfileResponse
    {
        public bool success;
        public PlayerProfile data;

        // Optional error message if success is false
        public string error;
    }

    /// <summary>
    /// Manages player profile data, progress, and synchronization with backend.
    /// Handles loading, saving, and caching of player information.
    /// </summary>
    public class PlayerDataManager : MonoBehaviour
    {
        // Singleton
        private static PlayerDataManager s_instance;
        public static PlayerDataManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<PlayerDataManager>();
                    if (s_instance == null)
                    {
                        GameObject go = new GameObject("[PlayerDataManager]");
                        s_instance = go.AddComponent<PlayerDataManager>();
                    }
                }
                return s_instance;
            }
        }

        // Player data
        public PlayerProfile CurrentProfile { get; private set; }
        public bool HasPlayerData => CurrentProfile != null;

        // Events
        // Fired after a successful login load — MainMenuController listens to this
        public event Action<PlayerProfile> OnPlayerDataLoaded;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Loads player data from the backend API and fires OnPlayerDataLoaded event on success.
        /// </summary>
        public async Task LoadPlayerDataFromAPI()
        {
            if (!DeviceAuthManager.Instance.IsAuthenticated)
            {
                Debug.LogWarning("[PlayerDataManager] Cannot load player data: not authenticated");
                return;
            }

            try
            {
                Debug.Log("[PlayerDataManager] Loading player profile from API...");

                // Fetch player profile
                string profileJson = await DeviceAuthManager.Instance.MakeAuthenticatedRequestAsync(
                    "/api/v1/user",
                    "GET"
                );

                var wrapper = JsonUtility.FromJson<PlayerProfileResponse>(profileJson);

                if (wrapper == null)
                {
                    throw new Exception("API returned an empty response");
                }

                if (!wrapper.success)
                {
                    throw new Exception(
                        $"API returned success: false. Error: {wrapper.error ?? "Unknown error"}"
                    );
                }

                if (wrapper.data == null)
                {
                    throw new Exception("API returned success: true but no data");
                }

                CurrentProfile = wrapper.data;

                Debug.Log($"[PlayerDataManager] Loaded profile for: {CurrentProfile.username}");
                // Fire event to notify listeners (e.g. MainMenuController) that player data is loaded
                OnPlayerDataLoaded?.Invoke(CurrentProfile);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerDataManager] Failed to load player data: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// Clears all player data
        /// </summary>
        public void ClearPlayerData()
        {
            CurrentProfile = null;
            Debug.Log("[PlayerDataManager] Player data cleared");
        }
    }
}
