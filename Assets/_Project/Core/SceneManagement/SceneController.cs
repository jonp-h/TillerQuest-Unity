using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TillerQuest.SceneManagement
{
    /// <summary>
    /// Handles all scene loading and transitions.
    /// Provides smooth transitions with loading screens and progress tracking.
    /// </summary>
    public class SceneController : MonoBehaviour
    {
        [Header("Scene Names")]
        [SerializeField]
        private string bootstrapSceneName = "Bootstrap";

        [SerializeField]
        private string mainMenuSceneName = "MainMenu";

        [SerializeField]
        private string loadingSceneName = "Loading";

        [Header("Settings")]
        [Tooltip("Minimum time to show loading screen (prevents flashing)")]
        [SerializeField]
        private float minimumLoadTime = 0.5f;

        // Singleton
        private static SceneController s_instance;
        public static SceneController Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<SceneController>();
                    if (s_instance == null)
                    {
                        GameObject go = new GameObject("[SceneController]");
                        s_instance = go.AddComponent<SceneController>();
                    }
                }
                return s_instance;
            }
        }

        // State
        public bool IsLoading { get; private set; }
        public float LoadProgress { get; private set; }
        public string CurrentSceneName { get; private set; }
        public string PreviousSceneName { get; private set; }

        // Events
        public event Action<string> OnSceneLoadStarted;
        public event Action<string, float> OnSceneLoadProgress;
        public event Action<string> OnSceneLoadCompleted;

        private Coroutine _loadCoroutine;
        private string _returnSceneName; // For mini-games to return

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentSceneName = SceneManager.GetActiveScene().name;
            Debug.Log($"[SceneController] Initialized in scene: {CurrentSceneName}");
        }

        /// <summary>
        /// Loads the main menu scene
        /// </summary>
        public void LoadMainMenu()
        {
            LoadScene(mainMenuSceneName);
        }

        public void LoadMiniGame(string gameName)
        {
            _returnSceneName = CurrentSceneName;
            LoadScene(gameName);
        }

        /// <summary>
        /// Returns from a mini-game to the previous scene (usually main menu)
        /// </summary>
        public void ReturnFromMiniGame()
        {
            if (string.IsNullOrEmpty(_returnSceneName))
            {
                Debug.LogWarning("[SceneController] No return scene set, loading main menu");
                LoadMainMenu();
            }
            else
            {
                Debug.Log($"[SceneController] Returning to: {_returnSceneName}");
                LoadScene(_returnSceneName);
                _returnSceneName = null;
            }
        }

        /// <summary>
        /// Loads a scene by name with a loading screen
        /// </summary>
        public void LoadScene(string sceneName)
        {
            if (IsLoading)
            {
                Debug.LogWarning(
                    $"[SceneController] Already loading a scene, ignoring request for: {sceneName}"
                );
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneController] Cannot load scene: name is null or empty");
                return;
            }

            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
            }

            _loadCoroutine = StartCoroutine(LoadSceneAsync(sceneName));
        }

        /// <summary>
        /// Loads a scene asynchronously with progress tracking
        /// </summary>
        private IEnumerator LoadSceneAsync(string sceneName)
        {
            IsLoading = true;
            LoadProgress = 0f;
            PreviousSceneName = CurrentSceneName;

            Debug.Log($"[SceneController] Starting scene load: {sceneName}");
            OnSceneLoadStarted?.Invoke(sceneName);

            float startTime = Time.realtimeSinceStartup;

            // Load the scene asynchronously
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName);
            loadOperation.allowSceneActivation = false;

            // Wait for scene to load (but don't activate yet)
            while (!loadOperation.isDone)
            {
                // Progress goes from 0 to 0.9 during loading
                LoadProgress = Mathf.Clamp01(loadOperation.progress / 0.9f);
                OnSceneLoadProgress?.Invoke(sceneName, LoadProgress);

                // When loading is almost done
                if (loadOperation.progress >= 0.9f)
                {
                    LoadProgress = 1f;
                    OnSceneLoadProgress?.Invoke(sceneName, LoadProgress);

                    // Ensure minimum load time (prevents flashing loading screens)
                    float elapsed = Time.realtimeSinceStartup - startTime;
                    if (elapsed < minimumLoadTime)
                    {
                        yield return new WaitForSeconds(minimumLoadTime - elapsed);
                    }

                    // Activate the scene
                    loadOperation.allowSceneActivation = true;
                }

                yield return null;
            }

            CurrentSceneName = sceneName;
            IsLoading = false;
            LoadProgress = 0f;

            Debug.Log($"[SceneController] Scene loaded: {sceneName}");
            OnSceneLoadCompleted?.Invoke(sceneName);

            _loadCoroutine = null;
        }

        /// <summary>
        /// Reloads the current scene
        /// </summary>
        public void ReloadCurrentScene()
        {
            LoadScene(CurrentSceneName);
        }

        /// <summary>
        /// Checks if a scene exists in build settings
        /// </summary>
        public bool SceneExists(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                string name = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                if (name == sceneName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
