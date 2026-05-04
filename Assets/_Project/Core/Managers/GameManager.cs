using System;
using TillerQuest.Auth;
using TillerQuest.Player;
using TillerQuest.SceneManagement;
using UnityEngine;

namespace TillerQuest.Core
{
    /// <summary>
    /// Central coordinator for all core game systems.
    /// Manages initialization order and provides global access to managers.
    /// This is the root singleton that orchestrates the entire application.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("System References")]
        [Tooltip("These will be auto-initialized if not assigned")]
        [SerializeField]
        private DeviceAuthManager authManager;

        [SerializeField]
        private PlayerDataManager playerDataManager;

        [SerializeField]
        private SceneController sceneController;

        [SerializeField]
        private AudioManager audioManager;

        [SerializeField]
        private MiniGameSessionService sessionService;

        // Singleton
        private static GameManager s_instance;
        public static GameManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<GameManager>();
                    if (s_instance == null)
                    {
                        GameObject go = new GameObject("[GameManager]");
                        s_instance = go.AddComponent<GameManager>();
                    }
                }
                return s_instance;
            }
        }

        // Public accessors
        public static DeviceAuthManager Auth => Instance.authManager;
        public static PlayerDataManager Player => Instance.playerDataManager;
        public static SceneController Scenes => Instance.sceneController;
        public static AudioManager Audio => Instance.audioManager;
        public static MiniGameSessionService Sessions => Instance.sessionService;

        private void Awake()
        {
            // Enforce singleton pattern
            if (s_instance != null && s_instance != this)
            {
                Debug.LogWarning("[GameManager] Duplicate instance found, destroying...");
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeSystems();
        }

        private void Start()
        {
            // Auto-load stored authentication on startup
            if (authManager != null && authManager.IsAuthenticated)
            {
                Debug.Log("[GameManager] Found existing authentication token");
                OnAuthenticationSuccess();
            }
            else
            {
                // Load MainMenu scene after Bootstrap initialization
                // Remove this if you want to start in a different scene or handle it manually
                sceneController?.LoadMainMenu();
            }
        }

        /// <summary>
        /// Initializes all core systems in the correct order
        /// </summary>
        private void InitializeSystems()
        {
            Debug.Log("[GameManager] Initializing core systems...");

            // Get or create DeviceAuthManager
            if (authManager == null)
            {
                authManager = DeviceAuthManager.Instance;
            }

            // Get or create PlayerDataManager
            if (playerDataManager == null)
            {
                playerDataManager = PlayerDataManager.Instance;
            }

            // Get or create SceneController
            if (sceneController == null)
            {
                sceneController = SceneController.Instance;
            }

            // Get or create AudioManager
            if (audioManager == null)
            {
                audioManager = AudioManager.Instance;
            }

            // Get or create MiniGameSessionService
            if (sessionService == null)
            {
                sessionService = MiniGameSessionService.Instance;
            }

            // Subscribe to auth events
            if (authManager != null)
            {
                authManager.OnAuthStateChanged += OnAuthStateChanged;
            }

            Debug.Log("[GameManager] All systems initialized successfully");
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (authManager != null)
            {
                authManager.OnAuthStateChanged -= OnAuthStateChanged;
            }
        }

        /// <summary>
        /// Handles authentication state changes
        /// </summary>
        private void OnAuthStateChanged(object sender, DeviceAuthEventArgs e)
        {
            Debug.Log($"[GameManager] Auth state changed: {e.State}");

            switch (e.State)
            {
                case AuthState.Authenticated:
                    OnAuthenticationSuccess();
                    break;

                case AuthState.Failed:
                    OnAuthenticationFailed();
                    break;
            }
        }

        /// <summary>
        /// Called when authentication succeeds
        /// </summary>
        private async void OnAuthenticationSuccess()
        {
            Debug.Log("[GameManager] Authentication successful, loading player data...");

            try
            {
                // Load player profile from backend
                await playerDataManager.LoadPlayerDataFromAPI();

                Debug.Log(
                    $"[GameManager] Player data loaded: {playerDataManager.CurrentProfile?.username}"
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameManager] Failed to load player data: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when authentication fails
        /// </summary>
        private void OnAuthenticationFailed()
        {
            Debug.LogWarning("[GameManager] Authentication failed");
        }

        /// <summary>
        /// Launches a mini-game
        /// Call this instead of calling SceneController.LoadMiniGame directly.
        /// </summary>
        public void LoadMiniGame(string miniGameSceneName)
        {
            if (miniGameSceneName == null)
            {
                Debug.LogError("[GameManager] Cannot load mini-game: name is null");
                return;
            }

            sceneController.LoadMiniGame(miniGameSceneName);
        }

        /// <summary>
        /// Logs out and returns to main menu
        /// </summary>
        public void Logout()
        {
            Debug.Log("[GameManager] Logging out...");

            authManager?.Logout();
            playerDataManager?.ClearPlayerData();

            sceneController?.LoadMainMenu();
        }

        /// <summary>
        /// Quits the application
        /// </summary>
        public void QuitApplication()
        {
            Debug.Log("[GameManager] Quitting application...");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
