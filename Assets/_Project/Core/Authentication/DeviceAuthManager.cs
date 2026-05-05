using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TillerQuest.Auth
{
    /// <summary>
    /// Manages OAuth 2.0 Device Authorization Flow for Unity applications.
    /// Implements RFC 8628 for secure authentication on devices with limited input.
    /// </summary>
    public class DeviceAuthManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Auth configuration asset. Use AuthConfig-Dev or AuthConfig-Prod.")]
        [SerializeField]
        private AuthConfig config;

        // Events
        public event EventHandler<DeviceAuthEventArgs> OnAuthStateChanged;

        // Properties
        public AuthState CurrentState { get; private set; } = AuthState.NotAuthenticated;
        public string AccessToken { get; private set; }
        public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);
        public string BaseUrl => config != null ? config.baseUrl : string.Empty;

        // Singleton pattern
        private static DeviceAuthManager s_instance;
        public static DeviceAuthManager Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindFirstObjectByType<DeviceAuthManager>();
                    if (s_instance == null)
                    {
                        GameObject go = new GameObject("DeviceAuthManager");
                        s_instance = go.AddComponent<DeviceAuthManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return s_instance;
            }
        }

        private Coroutine _authCoroutine;

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            ValidateConfiguration();
        }

        private void Start()
        {
            // Try to load existing token on startup
            LoadStoredToken();
        }

        private void ValidateConfiguration()
        {
            if (config == null)
            {
                Debug.LogError(
                    "[DeviceAuthManager] No AuthConfig assigned! Drag an AuthConfig asset into the Inspector."
                );
                return;
            }

            if (string.IsNullOrEmpty(config.baseUrl))
                Debug.LogError("[DeviceAuthManager] Base URL is not configured!");
            else if (!config.baseUrl.StartsWith("https://") && !Application.isEditor)
                Debug.LogWarning("[DeviceAuthManager] Base URL should use HTTPS in production!");

            if (string.IsNullOrEmpty(config.frontendUrl))
                Debug.LogError("[DeviceAuthManager] Frontend URL is not configured!");
            else if (!config.frontendUrl.StartsWith("https://") && !Application.isEditor)
                Debug.LogWarning(
                    "[DeviceAuthManager] Frontend URL should use HTTPS in production!"
                );

            if (string.IsNullOrEmpty(config.clientId))
                Debug.LogError("[DeviceAuthManager] Client ID is not configured!");
        }

        /// <summary>
        /// Loads a previously stored token if it's still valid
        /// </summary>
        private void LoadStoredToken()
        {
            if (SecureTokenStorage.HasValidToken())
            {
                AccessToken = SecureTokenStorage.LoadToken();
                if (!string.IsNullOrEmpty(AccessToken))
                {
                    SetAuthState(AuthState.Authenticated, "Loaded stored authentication token");
                    Debug.Log("[DeviceAuthManager] Successfully loaded stored token");
                }
            }
        }

        /// <summary>
        /// Starts the device authorization flow
        /// </summary>
        public void StartAuthentication()
        {
            if (_authCoroutine != null)
            {
                Debug.LogWarning("[DeviceAuthManager] Authentication already in progress");
                return;
            }

            _authCoroutine = StartCoroutine(AuthenticateCoroutine());
        }

        /// <summary>
        /// Cancels ongoing authentication
        /// </summary>
        public void CancelAuthentication()
        {
            if (_authCoroutine != null)
            {
                StopCoroutine(_authCoroutine);
                _authCoroutine = null;
                SetAuthState(AuthState.NotAuthenticated, "Authentication cancelled");
                Debug.Log("[DeviceAuthManager] Authentication cancelled by user");
            }
        }

        /// <summary>
        /// Logs out and clears stored credentials
        /// </summary>
        public void Logout()
        {
            AccessToken = null;
            SecureTokenStorage.ClearToken();
            SetAuthState(AuthState.NotAuthenticated, "Logged out successfully");
            Debug.Log("[DeviceAuthManager] User logged out");
        }

        /// <summary>
        /// Makes an authenticated API request
        /// </summary>
        public async Task<string> MakeAuthenticatedRequestAsync(
            string endpoint,
            string method = "GET"
        )
        {
            if (!IsAuthenticated)
            {
                throw new InvalidOperationException(
                    "Not authenticated. Call StartAuthentication() first."
                );
            }

            string url = $"{config.baseUrl}{endpoint}";

            using (UnityWebRequest request = new UnityWebRequest(url, method))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
                request.SetRequestHeader("Content-Type", "application/json");

                await SendWebRequestAsync(request);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.text;
                }
                else if (request.responseCode == 401)
                {
                    // Token expired or invalid
                    Debug.LogWarning("[DeviceAuthManager] Token expired, clearing authentication");
                    Logout();
                    throw new UnauthorizedAccessException("Authentication token expired");
                }
                else
                {
                    throw new Exception(
                        $"API request failed: {request.error} (Code: {request.responseCode})"
                    );
                }
            }
        }

        private IEnumerator AuthenticateCoroutine()
        {
            SetAuthState(AuthState.RequestingDeviceCode, "Requesting device code...");

            // Step 1: Request device code
            DeviceCodeResponse deviceResponse = null;
            yield return RequestDeviceCodeCoroutine(result => deviceResponse = result);

            if (deviceResponse == null)
            {
                SetAuthState(AuthState.Failed, "Failed to request device code");
                _authCoroutine = null;
                yield break;
            }

            // Step 2: Notify UI and open browser
            // Use frontend URL for user authorization (not backend API URL)
            string verificationUrl =
                $"{config.frontendUrl}/auth/device-authorization?user_code={deviceResponse.user_code}";

            SetAuthState(
                AuthState.AwaitingUserAuthorization,
                "Waiting for user authorization",
                deviceResponse.user_code,
                verificationUrl
            );

            OpenVerificationUrl(verificationUrl);

            // Step 3: Poll for token
            SetAuthState(AuthState.PollingForToken, "Polling for authorization...");

            bool success = false;
            yield return PollForTokenCoroutine(
                deviceResponse.device_code,
                deviceResponse.interval,
                deviceResponse.expires_in,
                result => success = result
            );

            if (success)
            {
                SetAuthState(AuthState.Authenticated, "Successfully authenticated!");
                Debug.Log("[DeviceAuthManager] Authentication successful!");
            }
            else
            {
                SetAuthState(AuthState.Failed, "Authentication failed or timed out");
                Debug.LogError("[DeviceAuthManager] Authentication failed");
            }

            _authCoroutine = null;
        }

        private IEnumerator RequestDeviceCodeCoroutine(Action<DeviceCodeResponse> callback)
        {
            var requestData = new DeviceCodeRequest { client_id = config.clientId };

            string jsonBody = JsonUtility.ToJson(requestData);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            using (
                UnityWebRequest request = new UnityWebRequest(
                    $"{config.baseUrl}/api/auth/device/code",
                    "POST"
                )
            )
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        DeviceCodeResponse response = JsonUtility.FromJson<DeviceCodeResponse>(
                            request.downloadHandler.text
                        );
                        Debug.Log(
                            $"[DeviceAuthManager] Device code received. User code: {response.user_code}"
                        );
                        callback(response);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(request.result.ToString());
                        Debug.LogError(
                            $"[DeviceAuthManager] Failed to parse device code response: {ex.Message}"
                        );
                        callback(null);
                    }
                }
                else
                {
                    Debug.LogError(
                        $"[DeviceAuthManager] Device code request failed: {request.error}"
                    );
                    Debug.LogError(
                        $"[DeviceAuthManager] Response body: {request.downloadHandler.text}"
                    );
                    callback(null);
                }
            }
        }

        private IEnumerator PollForTokenCoroutine(
            string deviceCode,
            int initialInterval,
            int expiresIn,
            Action<bool> callback
        )
        {
            var requestData = new TokenRequest
            {
                grant_type = "urn:ietf:params:oauth:grant-type:device_code",
                device_code = deviceCode,
                client_id = config.clientId,
            };

            float pollingInterval = initialInterval;
            float startTime = Time.realtimeSinceStartup;
            float timeout = Mathf.Min(expiresIn, config.authTimeoutSeconds);

            while (Time.realtimeSinceStartup - startTime < timeout)
            {
                yield return new WaitForSeconds(pollingInterval);

                string jsonBody = JsonUtility.ToJson(requestData);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

                using (
                    UnityWebRequest request = new UnityWebRequest(
                        $"{config.baseUrl}/api/auth/device/token",
                        "POST"
                    )
                )
                {
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");

                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            TokenResponse response = JsonUtility.FromJson<TokenResponse>(
                                request.downloadHandler.text
                            );
                            AccessToken = response.access_token;

                            // Store token securely
                            SecureTokenStorage.SaveToken(AccessToken, response.expires_in);

                            Debug.Log("[DeviceAuthManager] Token received and stored securely");
                            callback(true);
                            yield break;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError(
                                $"[DeviceAuthManager] Failed to parse token response: {ex.Message}"
                            );
                            callback(false);
                            yield break;
                        }
                    }
                    else
                    {
                        // Parse error response
                        try
                        {
                            TokenError error = JsonUtility.FromJson<TokenError>(
                                request.downloadHandler.text
                            );

                            switch (error.error)
                            {
                                case "authorization_pending":
                                    // Continue polling
                                    Debug.Log(
                                        "[DeviceAuthManager] Still waiting for user authorization..."
                                    );
                                    break;

                                case "slow_down":
                                    pollingInterval += 5f;
                                    Debug.Log(
                                        $"[DeviceAuthManager] Slowing down polling interval to {pollingInterval}s"
                                    );
                                    break;

                                case "access_denied":
                                    Debug.LogWarning(
                                        "[DeviceAuthManager] User denied authorization"
                                    );
                                    callback(false);
                                    yield break;

                                case "expired_token":
                                    Debug.LogWarning("[DeviceAuthManager] Device code expired");
                                    callback(false);
                                    yield break;

                                case "invalid_client":
                                {
                                    Debug.LogError(
                                        "[DeviceAuthManager] Client ID is not authorized by the server. Check your client ID configuration."
                                    );
                                    callback(false);
                                    yield break;
                                }

                                default:
                                    Debug.LogError(
                                        $"[DeviceAuthManager] Token error: {error.error} - {error.error_description}"
                                    );
                                    callback(false);
                                    yield break;
                            }
                        }
                        catch
                        {
                            Debug.LogError(
                                $"[DeviceAuthManager] Unexpected error during polling: {request.error}"
                            );
                            callback(false);
                            yield break;
                        }
                    }
                }
            }

            Debug.LogWarning("[DeviceAuthManager] Authentication timed out");
            callback(false);
        }

        private void OpenVerificationUrl(string url)
        {
            try
            {
                Application.OpenURL(url);
                Debug.Log($"[DeviceAuthManager] Opened browser to: {url}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DeviceAuthManager] Failed to open browser: {ex.Message}");
            }
        }

        private void SetAuthState(
            AuthState state,
            string message,
            string userCode = null,
            string verificationUri = null
        )
        {
            CurrentState = state;
            Debug.Log($"[DeviceAuthManager] State changed to: {state} - {message}");

            OnAuthStateChanged?.Invoke(
                this,
                new DeviceAuthEventArgs
                {
                    State = state,
                    Message = message,
                    UserCode = userCode,
                    VerificationUri = verificationUri,
                }
            );
        }

        // Helper for async/await with UnityWebRequest
        private Task SendWebRequestAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<bool>();

            request.SendWebRequest().completed += _ =>
            {
                tcs.SetResult(true);
            };

            return tcs.Task;
        }
    }
}
