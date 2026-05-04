using System;

namespace TillerQuest.Auth
{
    /// <summary>
    /// Data models for OAuth 2.0 Device Authorization Flow (RFC 8628)
    /// </summary>
    [Serializable]
    public class DeviceCodeRequest
    {
        public string client_id;
    }

    [Serializable]
    public class DeviceCodeResponse
    {
        public string device_code;
        public string user_code;
        public string verification_uri;
        public string verification_uri_complete;
        public int expires_in;
        public int interval;
    }

    [Serializable]
    public class TokenRequest
    {
        public string grant_type;
        public string device_code;
        public string client_id;
    }

    [Serializable]
    public class TokenResponse
    {
        public string access_token;
        public string token_type;
        public int expires_in;
    }

    [Serializable]
    public class TokenError
    {
        public string error;
        public string error_description;
    }

    public enum AuthState
    {
        NotAuthenticated,
        RequestingDeviceCode,
        AwaitingUserAuthorization,
        PollingForToken,
        Authenticated,
        Failed,
    }

    public class DeviceAuthEventArgs : EventArgs
    {
        public AuthState State { get; set; }
        public string Message { get; set; }
        public string UserCode { get; set; }
        public string VerificationUri { get; set; }
    }
}
