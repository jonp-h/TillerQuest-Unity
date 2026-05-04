using System;
using System.Text;
using System.Threading.Tasks;
using TillerQuest.Auth;
using UnityEngine;
using UnityEngine.Networking;

namespace TillerQuest.Core
{
    /// <summary>
    /// Communicates mini-game session lifecycle to the TillerQuest backend.
    /// Reports session start, singleplayer end, and multiplayer end with scores.
    /// The backend is responsible for computing and granting XP (anti-cheat).
    ///
    /// All methods are stub-safe: if the backend is unavailable or the player is
    /// not authenticated, the call is skipped and a placeholder result is returned.
    /// Replace the placeholder URLs with real endpoints when the backend is ready.
    /// </summary>
    public class MiniGameSessionService : MonoBehaviour
    {
        // ────────────── Endpoints  ─────────────
        private const string RESULTS_ENDPOINT = "/api/v1/games/tillerio/results";

        // Singleton
        private static MiniGameSessionService s_instance;
        public static MiniGameSessionService Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<MiniGameSessionService>();
                    if (s_instance == null)
                    {
                        var go = new GameObject("[MiniGameSessionService]");
                        s_instance = go.AddComponent<MiniGameSessionService>();
                    }
                }
                return s_instance;
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
        }

        // Called by the host (server) at game end with every player's final score.
        public async Task ReportGameResultsAsync(GamePlayerResult[] results)
        {
            if (!IsAuthenticated())
            {
                Debug.LogWarning(
                    "[SessionService] Skipping results report: host not authenticated"
                );
                return;
            }

            var body = JsonUtility.ToJson(new GameResultsRequest { playerList = results });

            try
            {
                await PostAsync(RESULTS_ENDPOINT, body);
                Debug.Log($"[SessionService] Reported results for {results.Length} players.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SessionService] Failed to report results: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private bool IsAuthenticated() =>
            DeviceAuthManager.Instance != null && DeviceAuthManager.Instance.IsAuthenticated;

        private async Task<string> PostAsync(string endpoint, string bodyJson)
        {
            string baseUrl =
                DeviceAuthManager.Instance?.BaseUrl
                ?? throw new InvalidOperationException("DeviceAuthManager not available");
            string url = baseUrl + endpoint;
            byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader(
                "Authorization",
                $"Bearer {DeviceAuthManager.Instance.AccessToken}"
            );

            var op = request.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.Success)
                return request.downloadHandler.text;

            if (request.responseCode == 401)
                throw new UnauthorizedAccessException("Authentication token expired");

            throw new Exception($"HTTP {request.responseCode}: {request.error}");
        }
    }

    // ── Data Models ───────────────────────────────────────────────────────────

    [Serializable]
    public class GamePlayerResult
    {
        public string userId;
        public int score;
        public bool survived; // true = alive at end of timer, false = eliminated mid-game
    }

    [Serializable]
    public class GameResultsRequest
    {
        public GamePlayerResult[] playerList;
    }
}
