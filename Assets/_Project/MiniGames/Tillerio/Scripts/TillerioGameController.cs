using System.Collections.Generic;
using System.Threading.Tasks;
using TillerQuest.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public struct PlayerData : INetworkSerializable, System.IEquatable<PlayerData>
{
    public ulong NetworkClientId;
    public FixedString64Bytes PlayerName;
    public FixedString64Bytes UserId;
    public int SpriteIndex;
    public uint TailColorPacked; // Color32 packed as RGBA uint
    public int Score;
    public bool IsAlive;
    public Color TailColor
    {
        get
        {
            var c = new Color32(
                (byte)(TailColorPacked >> 24),
                (byte)(TailColorPacked >> 16),
                (byte)(TailColorPacked >> 8),
                (byte)(TailColorPacked)
            );
            return c;
        }
    }

    // This method is called by Netcode to serialize and deserialize the PlayerData struct across the network.
    // The same method is used for both serialization and deserialization, with the BufferSerializer handling the direction of data flow.
    // When sending data, the serializer will write the values of the fields to the network buffer.
    // When receiving data, the serializer will read the values from the network buffer and populate the fields of the struct.
    // This ensures that the PlayerData can be correctly transmitted between the server and clients, allowing for synchronization of player information
    // This will then act as the authoritative source of player information for all clients
    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref NetworkClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref UserId);
        serializer.SerializeValue(ref SpriteIndex);
        serializer.SerializeValue(ref Score);
        serializer.SerializeValue(ref IsAlive);
        serializer.SerializeValue(ref TailColorPacked);
    }

    // Implementing equality based on NetworkClientId, which should be unique for each player
    // This allows us to easily find and update player data in the list based on their client ID
    // If equals return true (values are the same), the NetworkList will not trigger an update - and not send a packet
    public bool Equals(PlayerData other) =>
        NetworkClientId == other.NetworkClientId
        && Score == other.Score
        && IsAlive == other.IsAlive
        && SpriteIndex == other.SpriteIndex
        && TailColor == other.TailColor;
}

public class TillerioGameController : NetworkBehaviour
{
    public static TillerioGameController Instance { get; private set; }

    public NetworkList<PlayerData> PlayerDataList;

    // ----------------------- Networked Game State Management -----------------------

    public enum GameState
    {
        Lobby,
        Playing,
        GameOver,
    }

    public NetworkVariable<GameState> CurrentGameState = new NetworkVariable<GameState>(
        GameState.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> FireAlarmActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> FireAlarmTimeRemaining = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ----------------------- Player Management -----------------------

    [SerializeField]
    private GameObject playerPrefab;

    [SerializeField]
    private List<Transform> spawnAreas;

    [SerializeField]
    [Tooltip("Duration of the fire alarm event in seconds")]
    private float fireAlarmDuration = 90f;

    //read only
    [SerializeField]
    private List<Sprite> playerSprites;

    public IReadOnlyList<Sprite> PlayerSprites => playerSprites;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // must be initialized in Awake, not in the field declaration (Netcode requirement)
        PlayerDataList = new NetworkList<PlayerData>();
    }

    // ----------------------- Unity lifecycle and network callbacks -----------------------

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // If the game is already playing, spawn them immediately (late join)
        // The host should not spawn
        if (
            CurrentGameState.Value == GameState.Playing
            && clientId != NetworkManager.Singleton.LocalClientId
        )
            SpawnPlayerForClient(clientId, Random.Range(0, spawnAreas.Count));
        // remove the lobby ui
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Remove the player data for the disconnected client if the game has not yet started
        if (CurrentGameState.Value != GameState.Lobby)
            return;

        for (int i = 0; i < PlayerDataList.Count; i++)
        {
            if (PlayerDataList[i].NetworkClientId == clientId)
            {
                PlayerDataList.RemoveAt(i);
                break;
            }
        }
    }

    private void Update()
    {
        if (!IsServer || CurrentGameState.Value != GameState.Playing)
            return;

        TimeRemaining.Value -= Time.deltaTime;
        if (TimeRemaining.Value <= 0f)
        {
            TimeRemaining.Value = 0f;
            EndGame();
        }

        if (FireAlarmActive.Value)
        {
            FireAlarmTimeRemaining.Value -= Time.deltaTime;
            if (FireAlarmTimeRemaining.Value <= 0f)
            {
                FireAlarmActive.Value = false;
                FireAlarmTimeRemaining.Value = 0f;
                KillPlayersOutsideMeetingPoints();
            }
        }
    }

    // ----------------------- Player spawning and game start logic -----------------------

    private void SpawnPlayerForClient(ulong clientId, int spawnIndex)
    {
        Collider2D col = spawnAreas[spawnIndex].GetComponent<Collider2D>();
        Bounds bounds = col.bounds;
        Vector2 pos = new Vector2(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y)
        );

        GameObject player = Instantiate(playerPrefab, pos, Quaternion.identity);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }

    public void EndGame()
    {
        if (CurrentGameState.Value != GameState.Playing)
            return;

        CurrentGameState.Value = GameState.GameOver;

        // Host reports all player scores to the backend
        if (IsServer)
            _ = ReportResultsAsync();

        // Set Player.IsAlive = false on all alive player objects directly.
        // Player.OnIsAliveChanged handles SetActive(false), input disable, and UpdatePlayerAlive.
        foreach (var client in NetworkManager.Singleton.ConnectedClients.Values)
        {
            if (client.PlayerObject == null)
                continue;
            var p = client.PlayerObject.GetComponent<Player>();
            if (p != null && p.IsAlive.Value)
                p.IsAlive.Value = false;
        }

        Debug.Log("[TillerioGameController] Game Over!");
        // reward players and show endgame screen
    }

    private void CheckEndCondition()
    {
        // Example end condition: only one player left alive
        int aliveCount = 0;
        foreach (var playerData in PlayerDataList)
        {
            if (playerData.IsAlive)
                aliveCount++;
        }
        // If only one player is alive and there are at least 2 players in the game, end the game
        // This does not disturb testing sessions with only one player
        if (aliveCount <= 1 && PlayerDataList.Count > 1)
        {
            Debug.Log(
                "[TillerioGameController] End condition met (only one player alive), ending game."
            );
            EndGame();
        }
    }

    public void TriggerFireAlarm()
    {
        if (!IsServer || FireAlarmActive.Value || CurrentGameState.Value != GameState.Playing)
            return;

        FireAlarmActive.Value = true;
        FireAlarmTimeRemaining.Value = fireAlarmDuration;
        Debug.Log("[TillerioGameController] Fire alarm triggered!");
    }

    private void KillPlayersOutsideMeetingPoints()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClients.Values)
        {
            if (client.PlayerObject == null)
                continue;
            var player = client.PlayerObject.GetComponent<Player>();
            if (player == null || !player.IsAlive.Value)
                continue; // already dead

            if (!MeetingPoint.IsPlayerInMeetingPoint(player))
            {
                Debug.Log(
                    "[TillerioGameController] Player "
                        + player.OwnerClientId
                        + " was outside meeting points during fire alarm and has been killed."
                );
                player.IsAlive.Value = false;
            }
        }
    }

    // ----------------------- Server RPCs -----------------------

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartGameServerRpc(float gameDuration = 300f, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != 0) // Only allow the host (clientId 0) to start the game
            return;

        if (CurrentGameState.Value != GameState.Lobby)
            return;

        if (spawnAreas.Count == 0)
        {
            Debug.LogWarning("No spawn areas defined for players.");
            return;
        }

        var shuffledSpawnAreas = ShuffleList(new List<Transform>(spawnAreas));
        Debug.Log(
            $"Shuffled spawn areas for player spawning: {string.Join(", ", shuffledSpawnAreas)}"
        );

        int index = 0;

        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // Host is a spectator — never spawn a player for them
            if (clientId == NetworkManager.Singleton.LocalClientId)
                continue;

            if (index >= shuffledSpawnAreas.Count)
            {
                Debug.Log(
                    "More players than spawn areas! Reusing spawn areas for additional players."
                );
                // If we have more players than spawn areas, start reusing spawn areas from the beginning
                SpawnPlayerForClient(clientId, Random.Range(0, spawnAreas.Count));
            }
            else
            {
                SpawnPlayerForClient(clientId, index);
                index++;
            }
        }
        TimeRemaining.Value = gameDuration;
        CurrentGameState.Value = GameState.Playing;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RegisterPlayerServerRpc(
        FixedString64Bytes playerName,
        FixedString64Bytes userId,
        RpcParams rpcParams = default
    )
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        // Fixing saturation at 0.8 and value at 0.9 guarantees vivid, readable colours — only the hue is random.
        Color32 randomColor = Color.HSVToRGB(Random.value, 0.8f, 0.9f); // vivid, never too dark
        uint packed =
            ((uint)randomColor.r << 24)
            | ((uint)randomColor.g << 16)
            | ((uint)randomColor.b << 8)
            | randomColor.a;

        // Add the local player data to the server's list, which will then sync to all clients
        // Clients will rely on the server's PlayerDataList as the authoritative source of player information, including their names and scores
        PlayerDataList.Add(
            new PlayerData
            {
                NetworkClientId = clientId,
                PlayerName = playerName,
                UserId = userId,
                Score = 0,
                IsAlive = true,
                SpriteIndex = playerSprites.Count > 0 ? Random.Range(0, playerSprites.Count) : 0,
                TailColorPacked = packed,
            }
        );
    }

    // ------------------------ Utility functions -----------------------

    private async Task ReportResultsAsync()
    {
        var results = new GamePlayerResult[PlayerDataList.Count];
        for (int i = 0; i < PlayerDataList.Count; i++)
        {
            var p = PlayerDataList[i];
            results[i] = new GamePlayerResult
            {
                userId = p.UserId.ToString(),
                score = p.Score,
                survived = p.IsAlive, // snapshot before the loop marks everyone dead
            };
        }
        await MiniGameSessionService.Instance.ReportGameResultsAsync(results);
    }

    private static List<T> ShuffleList<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Range(0, n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
        return list;
    }

    public void UpdatePlayerScore(ulong clientId, int newScore)
    {
        for (int i = 0; i < PlayerDataList.Count; i++)
        {
            if (PlayerDataList[i].NetworkClientId != clientId)
                continue;
            var entry = PlayerDataList[i];
            entry.Score = newScore;
            PlayerDataList[i] = entry;
            return;
        }
    }

    public void UpdatePlayerAlive(ulong clientId, bool isAlive)
    {
        for (int i = 0; i < PlayerDataList.Count; i++)
        {
            if (PlayerDataList[i].NetworkClientId != clientId)
                continue;
            var entry = PlayerDataList[i];
            entry.IsAlive = isAlive;
            PlayerDataList[i] = entry;

            if (!isAlive)
            {
                CheckEndCondition();
            }

            return;
        }
    }
}
