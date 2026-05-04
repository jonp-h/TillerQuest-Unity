using TillerQuest.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class NetworkUserInterface : MonoBehaviour
{
    private UIDocument uiDocument;

    private VisualElement hostPanel;

    private Button hostButton;

    private Button clientJoinButton;

    private TextField ipAddressInput;

    private Button quitButton;

    private Label ipAddressText;

    private Button startGameButton;

    private TextField gameDurationInput;

    private VisualElement playerList;

    private VisualElement lobbyPanel;

    private Label playerNameText;

    private Label authWarning;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();

        hostPanel = uiDocument.rootVisualElement.Q<VisualElement>("HostPanel");
        hostButton = uiDocument.rootVisualElement.Q<Button>("HostButton");
        ipAddressInput = uiDocument.rootVisualElement.Q<TextField>("ClientIpInputField");
        clientJoinButton = uiDocument.rootVisualElement.Q<Button>("ClientJoinButton");
        quitButton = uiDocument.rootVisualElement.Q<Button>("QuitButton");
        ipAddressText = uiDocument.rootVisualElement.Q<Label>("IpAddressText");
        lobbyPanel = uiDocument.rootVisualElement.Q<VisualElement>("LobbyPanel");
        startGameButton = uiDocument.rootVisualElement.Q<Button>("StartGameButton");
        gameDurationInput = uiDocument.rootVisualElement.Q<TextField>("GameDurationInputField");
        playerNameText = uiDocument.rootVisualElement.Q<Label>("PlayerNameText");
        authWarning = uiDocument.rootVisualElement.Q<Label>("AuthWarning");
        playerList = uiDocument.rootVisualElement.Q<VisualElement>("PlayerList");
    }

    private void RefreshLobbyPlayers()
    {
        if (playerList == null)
            return;

        playerList.Clear();

        foreach (var player in TillerioGameController.Instance.PlayerDataList)
        {
            playerList.Add(BuildLobbyPlayerEntry(player));
        }
    }

    private VisualElement BuildLobbyPlayerEntry(PlayerData data)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;

        // Icon
        var icon = new VisualElement();
        icon.style.width = 64;
        icon.style.height = 64;
        icon.style.marginRight = 8;
        var sprites = TillerioGameController.Instance.PlayerSprites;
        if (data.SpriteIndex >= 0 && data.SpriteIndex < sprites.Count)
        {
            icon.style.backgroundImage = new StyleBackground(sprites[data.SpriteIndex]);
        }

        // Label
        var label = new Label($"{data.PlayerName}");

        row.Add(icon);
        row.Add(label);
        return row;
    }

    private void Start()
    {
        // could use .clicked, but using RegisterCallback allows for easier configuration and consistency with other UI elements that require event data (like the IP input field)
        hostButton.clicked += StartHost;
        clientJoinButton.clicked += StartClient;
        quitButton.clicked += QuitGame;
        startGameButton.clicked += StartTillerioGame;

        // Subscribe to network events
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }

        RefreshAuthDisplay();
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (TillerioGameController.Instance != null)
        {
            TillerioGameController.Instance.CurrentGameState.OnValueChanged -= OnGameStateChanged;
            TillerioGameController.Instance.PlayerDataList.OnListChanged -= OnLobbyPlayersChanged;
        }

        hostButton.clicked -= StartHost;
        clientJoinButton.clicked -= StartClient;
        quitButton.clicked -= QuitGame;
        startGameButton.clicked -= StartTillerioGame;
    }

    private void QuitGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Auth display ──────────────────────────────────────────────────────────

    private void RefreshAuthDisplay()
    {
        bool isAuthenticated = GameManager.Player != null && GameManager.Player.HasPlayerData;

        if (playerNameText != null)
        {
            playerNameText.text = isAuthenticated
                ? $"Logged in as: {GameManager.Player.CurrentProfile.username}"
                : "Not logged in";
        }

        if (authWarning != null)
            authWarning.style.display = isAuthenticated ? DisplayStyle.None : DisplayStyle.Flex;

        // Only admins can see the host panel
        bool isAdmin = isAuthenticated && GameManager.Player.CurrentProfile.role == "ADMIN";
        if (hostPanel != null)
            hostPanel.style.display = isAdmin ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // ── Network callbacks ─────────────────────────────────────────────────────

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected!");

        // Only register and subscribe once — when this machine itself connects
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (TillerioGameController.Instance != null)
            {
                TillerioGameController.Instance.CurrentGameState.OnValueChanged +=
                    OnGameStateChanged;
                TillerioGameController.Instance.PlayerDataList.OnListChanged +=
                    OnLobbyPlayersChanged;

                // Handle the current state in case the player joined a game already in progress
                var currentState = TillerioGameController.Instance.CurrentGameState.Value;
                if (currentState != TillerioGameController.GameState.Lobby)
                    OnGameStateChanged(currentState, currentState);
            }

            // Admins/hosts spectate — do not add them to the player list
            if (!NetworkManager.Singleton.IsHost)
            {
                string playerName = GameManager.Player?.CurrentProfile?.username ?? $"{clientId}";
                string userId = GameManager.Player?.CurrentProfile?.userId ?? clientId.ToString();
                TillerioGameController.Instance.RegisterPlayerServerRpc(playerName, userId);
            }
        }

        // Show Start Game button to host whenever anyone connects
        if (startGameButton != null && NetworkManager.Singleton.IsHost)
            startGameButton.SetEnabled(true);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected!");
        // if the game is over, do not remove player from leaderboard
        if (
            TillerioGameController.Instance.CurrentGameState.Value
            == TillerioGameController.GameState.GameOver
        )
            return;

        RefreshLobbyPlayers();
    }

    // ── Host / client start ───────────────────────────────────────────────────

    private void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        Debug.Log($"Host started. Students should connect to: {GetLocalIPAddress()}");
    }

    private void StartClient()
    {
        try
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(ipAddressInput.text, 7777);
            NetworkManager.Singleton.StartClient();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to start client: {ex.Message}");
        }
    }

    // ── Start game (host only) ────────────────────────────────────────────────

    private void StartTillerioGame()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        if (TillerioGameController.Instance == null)
            return;

        // Call the gameController to update the game state to Playing
        TillerioGameController.Instance.StartGameServerRpc(
            float.TryParse(gameDurationInput.text, out float parsedDuration)
                ? parsedDuration * 60f // Convert minutes to seconds
                : 300f
        );

        if (startGameButton != null)
            startGameButton.SetEnabled(false);

        // Unsubscribe from lobby updates since we're no longer in the lobby
        TillerioGameController.Instance.PlayerDataList.OnListChanged -= OnLobbyPlayersChanged;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetLocalIPAddress()
    {
        var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                ipAddressText.text = ip.ToString();
                return ip.ToString();
            }
        }
        return "IP not found";
    }

    private void OnGameStateChanged(
        TillerioGameController.GameState oldGameState,
        TillerioGameController.GameState newGameState
    )
    {
        if (newGameState == TillerioGameController.GameState.Playing)
        {
            // Hide the lobby UI and show the game UI
            if (lobbyPanel != null)
                lobbyPanel.style.display = DisplayStyle.None;

            // Host becomes a spectator when the game starts
            if (NetworkManager.Singleton.IsHost)
                CameraFollow.Instance.EnableSpectatorMode();
        }
    }

    private void OnLobbyPlayersChanged(NetworkListEvent<PlayerData> _) => RefreshLobbyPlayers();

    // private void RefreshLobbyList()
    // {
    //     if (TillerioGameController.Instance == null)
    //         return;

    //     if (playerListView == null)
    //         return;

    //     _lobbyPlayerNames.Clear(); // Clear the local list before repopulating it

    //     foreach (var player in TillerioGameController.Instance.PlayerDataList)
    //     {
    //         _lobbyPlayerNames.Add(player.PlayerName.ToString());
    //     }
    //     playerListView.Rebuild(); // Refresh the ListView to reflect changes in the itemsSource
    // }
}
