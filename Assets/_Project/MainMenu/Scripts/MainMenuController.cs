using System.Linq;
using TillerQuest.Auth;
using TillerQuest.Core;
using TillerQuest.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TillerQuest.MainMenu
{
    public class MainMenuController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField]
        private GameObject loginPanel;

        [SerializeField]
        private GameObject gameSelectionPanel;

        [Header("Login UI")]
        [SerializeField]
        private Button loginButton;

        [SerializeField]
        private TMP_Text statusText;

        [SerializeField]
        private TMP_Text userCodeText;

        [SerializeField]
        private TMP_Text welcomeText;

        // TODO: add grid for multiple minigames

        [SerializeField]
        private Button playTillerioButton;

        [SerializeField]
        private Button logoutButton;

        [Header("Configuration")]
        private DeviceAuthManager authManager;

        private void Start()
        {
            authManager = GameManager.Auth;

            if (authManager != null)
                authManager.OnAuthStateChanged += OnAuthStateChanged;

            if (GameManager.Player != null)
                GameManager.Player.OnPlayerDataLoaded += OnPlayerDataLoaded;

            if (loginButton != null)
                loginButton.onClick.AddListener(() => authManager.StartAuthentication());

            if (logoutButton != null)
                logoutButton.onClick.AddListener(() => GameManager.Instance.Logout());

            if (playTillerioButton != null)
                playTillerioButton.onClick.AddListener(() =>
                    GameManager.Instance.LoadMiniGame("Tillerio")
                );

            UpdateUI();
        }

        private void OnDestroy()
        {
            if (authManager != null)
                authManager.OnAuthStateChanged -= OnAuthStateChanged;

            if (GameManager.Player != null)
                GameManager.Player.OnPlayerDataLoaded -= OnPlayerDataLoaded;
        }

        private void OnAuthStateChanged(object sender, DeviceAuthEventArgs e)
        {
            switch (e.State)
            {
                case AuthState.AwaitingUserAuthorization:
                    if (statusText != null)
                        statusText.text = "Please authorize in browser";
                    if (userCodeText != null)
                        userCodeText.text = $"Code: {e.UserCode}";
                    break;

                case AuthState.Authenticated:
                    if (statusText != null)
                        statusText.text = "Login successful!";
                    if (userCodeText != null)
                        userCodeText.text = "";
                    UpdateUI();
                    break;

                case AuthState.Failed:
                    if (statusText != null)
                        statusText.text = $"Login failed: {e.Message}";
                    break;
            }
        }

        private void OnPlayerDataLoaded(PlayerProfile profile)
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            bool isAuth = authManager != null && authManager.IsAuthenticated;

            if (loginPanel != null)
                loginPanel.SetActive(!isAuth);

            if (gameSelectionPanel != null)
                gameSelectionPanel.SetActive(isAuth);

            if (isAuth && GameManager.Player?.CurrentProfile != null)
            {
                if (welcomeText != null)
                    welcomeText.text =
                        $"Welcome, {GameManager.Player.CurrentProfile.username}! ({GameManager.Player.CurrentProfile.userId})";
            }

            if (playTillerioButton != null)
                playTillerioButton.gameObject.SetActive(
                    isAuth
                        && GameManager.Player?.CurrentProfile?.access != null
                        && GameManager.Player.CurrentProfile.access.Contains("Tillerio")
                );

            // TODO: add text info for access rights
        }
    }
}
