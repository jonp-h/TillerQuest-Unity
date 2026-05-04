using System;
using System.Collections.Generic;
using TillerQuest.Core;
using TillerQuest.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class GameHUD : MonoBehaviour
{
    private UIDocument uiDocument;

    private Button quitButton;

    private Label timerLabel;

    private VisualElement leaderboardPanel;

    private ProgressBar boostBar;

    private Player player;

    private VisualElement endScreen;

    private VisualElement endLeaderboard;

    private Button endScreenQuitButton;

    private Button stopGameButton;

    private Label playerNameLabel;

    private VisualElement fireAlarmPanel;
    private Label fireAlarmTimerLabel;
    private VisualElement fireAlarmButtonContainer;
    private Button fireAlarmButton;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        quitButton = uiDocument.rootVisualElement.Q<Button>("QuitButton");
        timerLabel = uiDocument.rootVisualElement.Q<Label>("TimerLabel");
        playerNameLabel = uiDocument.rootVisualElement.Q<Label>("PlayerNameLabel");
        leaderboardPanel = uiDocument.rootVisualElement.Q<VisualElement>("LeaderboardPanel");
        boostBar = uiDocument.rootVisualElement.Q<ProgressBar>("BoostBar");
        endScreen = uiDocument.rootVisualElement.Q<VisualElement>("EndScreen");
        endLeaderboard = uiDocument.rootVisualElement.Q<VisualElement>("EndLeaderboard");
        endScreenQuitButton = uiDocument.rootVisualElement.Q<Button>("EndScreenQuitButton");
        stopGameButton = uiDocument.rootVisualElement.Q<Button>("StopGameButton");
        fireAlarmPanel = uiDocument.rootVisualElement.Q<VisualElement>("FireAlarmPanel");
        fireAlarmTimerLabel = uiDocument.rootVisualElement.Q<Label>("FireAlarmTimerLabel");
        fireAlarmButtonContainer = uiDocument.rootVisualElement.Q<VisualElement>("FireAlarm");
        fireAlarmButton = uiDocument.rootVisualElement.Q<Button>("FireAlarmButton");
        fireAlarmButton.clicked += TillerioGameController.Instance.TriggerFireAlarm;
        quitButton.clicked += QuitGame;
        endScreenQuitButton.clicked += QuitGame;
        stopGameButton.clicked += TillerioGameController.Instance.EndGame;
    }

    private void Start()
    {
        if (TillerioGameController.Instance != null)
        {
            TillerioGameController.Instance.CurrentGameState.OnValueChanged += OnGameStateChanged;
            TillerioGameController.Instance.TimeRemaining.OnValueChanged += OnTimeRemainingChanged;
            TillerioGameController.Instance.PlayerDataList.OnListChanged += OnPlayerDataChanged;
            TillerioGameController.Instance.FireAlarmActive.OnValueChanged +=
                OnFireAlarmActiveChanged;
            TillerioGameController.Instance.FireAlarmTimeRemaining.OnValueChanged +=
                OnFireAlarmTimerChanged;
        }
        if (GameManager.Player != null)
            playerNameLabel.text = $"{GameManager.Player.CurrentProfile.username}";
    }

    private void OnDestroy()
    {
        quitButton.clicked -= QuitGame;
        endScreenQuitButton.clicked -= QuitGame;
        stopGameButton.clicked -= TillerioGameController.Instance.EndGame;
        fireAlarmButton.clicked -= TillerioGameController.Instance.TriggerFireAlarm;

        if (TillerioGameController.Instance != null)
        {
            TillerioGameController.Instance.CurrentGameState.OnValueChanged -= OnGameStateChanged;
            TillerioGameController.Instance.TimeRemaining.OnValueChanged -= OnTimeRemainingChanged;
            TillerioGameController.Instance.PlayerDataList.OnListChanged -= OnPlayerDataChanged;
            TillerioGameController.Instance.FireAlarmActive.OnValueChanged -=
                OnFireAlarmActiveChanged;
            TillerioGameController.Instance.FireAlarmTimeRemaining.OnValueChanged -=
                OnFireAlarmTimerChanged;
        }

        if (player != null)
            player.Boost.OnValueChanged -= OnBoostChanged;
    }

    private void OnGameStateChanged(
        TillerioGameController.GameState prev,
        TillerioGameController.GameState next
    )
    {
        if (next == TillerioGameController.GameState.Playing)
        {
            uiDocument.rootVisualElement.style.display = DisplayStyle.Flex;
            FindLocalPlayer();
            RefreshLeaderboard();

            // Show Stop Game button only for the host and hide boost bar
            if (NetworkManager.Singleton.IsHost)
            {
                if (stopGameButton != null)
                    stopGameButton.style.display = DisplayStyle.Flex;
                if (boostBar != null)
                    boostBar.style.display = DisplayStyle.None;
                if (fireAlarmButtonContainer != null)
                    fireAlarmButtonContainer.style.display = DisplayStyle.Flex;
            }
        }
        else if (next == TillerioGameController.GameState.GameOver)
        {
            ShowEndScreen();
        }
    }

    private void OnTimeRemainingChanged(float prev, float next)
    {
        int minutes = Mathf.FloorToInt(next / 60f);
        int seconds = Mathf.FloorToInt(next % 60f);
        // 00 formats the number to be at least 2 digits, padding with 0 if necessary
        timerLabel.text = $"{minutes:00}:{seconds:00}";
    }

    private void QuitGame()
    {
        NetworkManager.Singleton.Shutdown();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void FindLocalPlayer()
    {
        var localObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localObj == null)
            return;
        player = localObj.GetComponent<Player>();
        if (player != null)
        {
            player.Boost.OnValueChanged += OnBoostChanged;
            boostBar.value = player.Boost.Value;
        }
    }

    private void OnPlayerDataChanged(NetworkListEvent<PlayerData> changeEvent)
    {
        RefreshLeaderboard();
    }

    private void OnBoostChanged(int previousValue, int newValue)
    {
        if (boostBar != null)
            boostBar.value = newValue;
    }

    private void RefreshLeaderboard()
    {
        if (leaderboardPanel == null || TillerioGameController.Instance == null)
            return;

        var playerDataList = TillerioGameController.Instance.PlayerDataList;
        var rows = new List<PlayerData>();
        for (int j = 0; j < playerDataList.Count; j++)
            rows.Add(playerDataList[j]);
        rows.Sort((a, b) => b.Score.CompareTo(a.Score));

        leaderboardPanel.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            leaderboardPanel.Add(BuildLeaderboardRow(i + 1, rows[i]));
        }
    }

    private VisualElement BuildLeaderboardRow(int rank, PlayerData data)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 4;

        // Icon
        var icon = new VisualElement();
        icon.style.width = 32;
        icon.style.height = 32;
        icon.style.marginRight = 8;
        var sprites = TillerioGameController.Instance.PlayerSprites;
        if (data.SpriteIndex >= 0 && data.SpriteIndex < sprites.Count)
        {
            icon.style.backgroundImage = new StyleBackground(sprites[data.SpriteIndex]);
        }

        // Label
        var label = new Label($"{rank}. {data.PlayerName}  {data.Score}");

        if (!data.IsAlive)
        {
            row.style.opacity = 0.4f;
        }

        row.Add(icon);
        row.Add(label);
        return row;
    }

    private void ShowEndScreen()
    {
        if (endScreen == null || TillerioGameController.Instance == null)
            return;

        // move the leaderboard to the end screen and refresh it one last time to show final scores
        leaderboardPanel.RemoveFromHierarchy();
        endLeaderboard.Add(leaderboardPanel);
        leaderboardPanel.style.alignItems = Align.Center;
        leaderboardPanel.style.alignSelf = Align.Center;
        RefreshLeaderboard();

        endScreen.style.display = DisplayStyle.Flex;
    }

    private void OnFireAlarmActiveChanged(bool prev, bool next)
    {
        if (next)
        {
            // Show the alarm banner
            if (fireAlarmPanel != null)
                fireAlarmPanel.style.display = DisplayStyle.Flex;

            // Disable button so it can't be triggered twice
            if (fireAlarmButton != null)
                fireAlarmButton.SetEnabled(false);

            // Hide doors on all clients
            foreach (var door in GameObject.FindGameObjectsWithTag("Door"))
                door.SetActive(false);
        }
        else
        {
            // Alarm over — hide the banner
            if (fireAlarmPanel != null)
                fireAlarmPanel.style.display = DisplayStyle.None;

            // Re-enable the button for potential future alarms
            if (fireAlarmButton != null)
                fireAlarmButton.SetEnabled(true);
        }
    }

    private void OnFireAlarmTimerChanged(float prev, float next)
    {
        if (fireAlarmTimerLabel == null)
            return;
        int minutes = Mathf.FloorToInt(next / 60f);
        int seconds = Mathf.FloorToInt(next % 60f);
        fireAlarmTimerLabel.text = $"{minutes:00}:{seconds:00}";
    }
}
