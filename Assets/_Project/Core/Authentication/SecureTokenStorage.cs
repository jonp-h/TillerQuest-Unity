using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TillerQuest.Auth
{
    /// <summary>
    /// Secure token storage using AES-256 encryption with PBKDF2 key derivation.
    /// Keys are derived from a device-specific identifier combined with a random
    /// per-token salt and a fixed app pepper. Never stores tokens in plain text.
    /// </summary>
    public static class SecureTokenStorage
    {
        private const string TOKEN_KEY = "tq_auth_token";
        private const string TOKEN_EXPIRY_KEY = "tq_auth_expiry";
        private const string TOKEN_SALT_KEY = "tq_auth_salt";
        private const int PBKDF2_ITERATIONS = 100_000;

        // App-specific pepper mixed into PBKDF2 alongside the random per-token salt.
        // This being public in source code is intentional — per Kerckhoffs's principle,
        // security relies on the random salt + PBKDF2 cost, not on this value's secrecy.
        private static readonly byte[] s_additionalEntropy = Encoding.UTF8.GetBytes(
            "TillerQuest-Unity-2026"
        );

        /// <summary>
        /// Saves an access token securely with expiration time.
        /// Generates a new random salt for each save.
        /// </summary>
        public static void SaveToken(string token, int expiresInSeconds)
        {
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("[SecureTokenStorage] Attempted to save null/empty token");
                return;
            }

            try
            {
                byte[] salt = new byte[16];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }

                string encryptedToken = EncryptStringAES(token, salt);
                long expiryTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;

                PlayerPrefs.SetString(TOKEN_KEY, encryptedToken);
                PlayerPrefs.SetString(TOKEN_EXPIRY_KEY, expiryTimestamp.ToString());
                PlayerPrefs.SetString(TOKEN_SALT_KEY, Convert.ToBase64String(salt));
                PlayerPrefs.Save();

#if UNITY_EDITOR
                Debug.Log("[SecureTokenStorage] Token saved successfully");
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SecureTokenStorage] Failed to save token: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads and decrypts the stored token. Returns null if expired or invalid.
        /// </summary>
        public static string LoadToken()
        {
            try
            {
                if (
                    !PlayerPrefs.HasKey(TOKEN_KEY)
                    || !PlayerPrefs.HasKey(TOKEN_EXPIRY_KEY)
                    || !PlayerPrefs.HasKey(TOKEN_SALT_KEY)
                )
                {
                    return null;
                }

                long expiryTimestamp = long.Parse(PlayerPrefs.GetString(TOKEN_EXPIRY_KEY));
                long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                if (currentTimestamp >= expiryTimestamp)
                {
                    Debug.Log("[SecureTokenStorage] Token expired, clearing storage");
                    ClearToken();
                    return null;
                }

                byte[] salt = Convert.FromBase64String(PlayerPrefs.GetString(TOKEN_SALT_KEY));
                string encryptedToken = PlayerPrefs.GetString(TOKEN_KEY);
                string decryptedToken = DecryptStringAES(encryptedToken, salt);

#if UNITY_EDITOR
                Debug.Log("[SecureTokenStorage] Token loaded successfully");
#endif
                return decryptedToken;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SecureTokenStorage] Failed to load token: {ex.Message}");
                ClearToken();
                return null;
            }
        }

        /// <summary>
        /// Clears all stored authentication data.
        /// </summary>
        public static void ClearToken()
        {
            PlayerPrefs.DeleteKey(TOKEN_KEY);
            PlayerPrefs.DeleteKey(TOKEN_EXPIRY_KEY);
            PlayerPrefs.DeleteKey(TOKEN_SALT_KEY);
            PlayerPrefs.Save();
            Debug.Log("[SecureTokenStorage] Token cleared");
        }

        /// <summary>
        /// Checks if a valid non-expired token exists without decrypting it.
        /// </summary>
        public static bool HasValidToken()
        {
            if (
                !PlayerPrefs.HasKey(TOKEN_KEY)
                || !PlayerPrefs.HasKey(TOKEN_EXPIRY_KEY)
                || !PlayerPrefs.HasKey(TOKEN_SALT_KEY)
            )
            {
                return false;
            }

            try
            {
                long expiryTimestamp = long.Parse(PlayerPrefs.GetString(TOKEN_EXPIRY_KEY));
                long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return currentTimestamp < expiryTimestamp;
            }
            catch
            {
                return false;
            }
        }

        private static string EncryptStringAES(string plainText, byte[] salt)
        {
            byte[] key = DeriveKeyFromDevice(salt);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV();

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = encryptor.TransformFinalBlock(
                    plainBytes,
                    0,
                    plainBytes.Length
                );

                // Prepend IV to encrypted data
                byte[] result = new byte[aes.IV.Length + encryptedBytes.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

                return Convert.ToBase64String(result);
            }
        }

        private static string DecryptStringAES(string encryptedText, byte[] salt)
        {
            byte[] key = DeriveKeyFromDevice(salt);
            byte[] fullData = Convert.FromBase64String(encryptedText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;

                // Extract IV from the beginning
                byte[] iv = new byte[16];
                byte[] encryptedBytes = new byte[fullData.Length - iv.Length];

                Buffer.BlockCopy(fullData, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(fullData, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

                aes.IV = iv;
                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                byte[] decryptedBytes = decryptor.TransformFinalBlock(
                    encryptedBytes,
                    0,
                    encryptedBytes.Length
                );

                return Encoding.UTF8.GetString(decryptedBytes);
            }
        }

        // Derives a 256-bit AES key using PBKDF2-SHA256.
        // Password: device-unique identifier + product name (device-bound).
        // Salt: random 16-byte value (unique per token) concatenated with the app pepper.
        private static byte[] DeriveKeyFromDevice(byte[] salt)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(
                SystemInfo.deviceUniqueIdentifier + Application.productName
            );

            // Combine random salt with app pepper so both contribute to the derived key
            byte[] combinedSalt = new byte[salt.Length + s_additionalEntropy.Length];
            Buffer.BlockCopy(salt, 0, combinedSalt, 0, salt.Length);
            Buffer.BlockCopy(
                s_additionalEntropy,
                0,
                combinedSalt,
                salt.Length,
                s_additionalEntropy.Length
            );

            using var pbkdf2 = new Rfc2898DeriveBytes(
                passwordBytes,
                combinedSalt,
                PBKDF2_ITERATIONS,
                HashAlgorithmName.SHA256
            );
            return pbkdf2.GetBytes(32);
        }
    }
}
