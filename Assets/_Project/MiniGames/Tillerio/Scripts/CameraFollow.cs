using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [SerializeField]
    private CinemachineCamera cineCam;
    private Player watchedPlayer;

    [Header("Spectator Settings")]
    [SerializeField]
    private float spectatorPanSpeed = 5f;

    [SerializeField]
    private float minZoom = 10f;

    [SerializeField]
    private float maxZoom = 300f;

    [SerializeField]
    private float zoomSpeed = 5f;

    private bool isSpectating;

    private InputAction moveAction;
    private InputAction zoomAction;

    private void Awake() => Instance = this;

    private void Start()
    {
        moveAction = InputSystem.actions.FindAction("Move");
        zoomAction = InputSystem.actions.FindAction("Zoom");
    }

    private void OnDestroy()
    {
        Instance = null;
        UnwatchCurrentPlayer();
        moveAction?.Disable();
        zoomAction?.Disable();
    }

    private void Update()
    {
        if (!isSpectating)
            return;

        // WASD panning
        Vector2 moveInput = moveAction.ReadValue<Vector2>();

        // Scale speed by current zoom so panning feels consistent when zoomed out
        float zoom = cineCam.Lens.OrthographicSize;
        cineCam.transform.Translate(
            spectatorPanSpeed * zoom * Time.deltaTime * new Vector3(moveInput.x, moveInput.y, 0f),
            Space.World
        );

        // Scroll wheel zoom
        float zoomInput = zoomAction.ReadValue<float>();
        if (zoomInput != 0f)
        {
            cineCam.Lens.OrthographicSize = Mathf.Clamp(
                cineCam.Lens.OrthographicSize - zoomInput * zoomSpeed,
                minZoom,
                maxZoom
            );
        }
    }

    // Called when the game starts and this machine is the host
    public void EnableSpectatorMode()
    {
        UnwatchCurrentPlayer();
        cineCam.Follow = null;
        isSpectating = true;
        moveAction?.Enable();
        zoomAction?.Enable();

        // Ensure the camera is behind the scene so it can render 2D objects
        var pos = cineCam.transform.position;
        cineCam.transform.position = new Vector3(pos.x, pos.y, -1f);
    }

    public void FollowPlayer(Player player)
    {
        UnwatchCurrentPlayer();
        watchedPlayer = player;
        cineCam.Follow = player.transform;
        watchedPlayer.IsAlive.OnValueChanged += OnWatchedPlayerDied;
    }

    private void OnWatchedPlayerDied(bool oldValue, bool newValue)
    {
        if (newValue)
            return;
        UnwatchCurrentPlayer();
        FindNextAlivePlayer();
    }

    public void FindNextAlivePlayer()
    {
        foreach (var player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (player.IsAlive.Value)
            {
                FollowPlayer(player);
                return;
            }
        }
        cineCam.Follow = null;
    }

    private void UnwatchCurrentPlayer()
    {
        if (watchedPlayer != null)
        {
            watchedPlayer.IsAlive.OnValueChanged -= OnWatchedPlayerDied;
            watchedPlayer = null;
        }
    }
}
