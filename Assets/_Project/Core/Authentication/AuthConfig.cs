using UnityEngine;

namespace TillerQuest.Auth
{
    [CreateAssetMenu(fileName = "AuthConfig", menuName = "TillerQuest/Auth Config")]
    public class AuthConfig : ScriptableObject
    {
        [Header("Backend")]
        [Tooltip("Backend API base URL (must use HTTPS in production)")]
        public string baseUrl = "https://your-backend-url.com";

        [Tooltip("Frontend base URL (must use HTTPS in production)")]
        public string frontendUrl = "https://your-frontend-url.com";

        [Tooltip("OAuth client ID registered with the backend")]
        public string clientId = "your-client-id";

        [Header("Polling")]
        [Tooltip("Maximum time to wait for user authorization (seconds)")]
        public int authTimeoutSeconds = 600;
    }
}
