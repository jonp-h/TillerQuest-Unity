using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[
    RequireComponent(typeof(Unity.Netcode.Components.NetworkTransform)),
    RequireComponent(typeof(NetworkObject)),
    RequireComponent(typeof(Collider2D))
]
public class Player : NetworkBehaviour
{
    [Header("References")]
    [SerializeField]
    private TailManager tailManager;

    private Collider2D headCollider;

    [SerializeField]
    private SpriteRenderer iconRenderer;

    [Header("Movement Settings")]
    [SerializeField]
    private float baseSpeed = 5f;

    [SerializeField]
    private float rotationSpeed = 200f;

    [SerializeField]
    private float boostMultiplier = 2f;

    [SerializeField]
    private float boostDepletionRate = 20f; // Boost units per second instead of per frame

    [Header("Input Limits - Security")]
    [SerializeField]
    private float maxInputValue = 1.1f; // Slight tolerance for input normalization

    [SerializeField]
    [Tooltip(
        "Minimum time between client input RPCs to prevent spamming. 0.05 = 20 calls per second."
    )]
    private float inputSendRate = 0.05f; // Limit RPC calls to 20 per second

    public NetworkVariable<int> Boost = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private InputAction moveAction;
    private InputAction boostAction;

    private float collisionCooldown = 0f;

    public bool IsInCooldown() => collisionCooldown > 0f;

    public NetworkVariable<bool> IsAlive = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server-authoritative input state
    private float serverHorizontalInput;
    private bool serverIsBoosting;

    // Client-side prediction
    private float predictedHorizontalInput;
    private bool predictedIsBoosting;

    // Rate limiting
    private float lastInputSendTime;
    private float boostTimer;

    private void Awake()
    {
        headCollider = GetComponent<Collider2D>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            moveAction = InputSystem.actions.FindAction("Move");
            boostAction = InputSystem.actions.FindAction("Boost");
            CameraFollow.Instance.FollowPlayer(this);
        }
        // Subscribe to IsAlive changes on all clients to handle death logic
        IsAlive.OnValueChanged += OnIsAliveChanged;
        ApplyPlayerLooks();
        TillerioGameController.Instance.PlayerDataList.OnListChanged += OnPlayerListChanged;
    }

    public override void OnNetworkDespawn()
    {
        IsAlive.OnValueChanged -= OnIsAliveChanged;
        if (TillerioGameController.Instance != null)
            TillerioGameController.Instance.PlayerDataList.OnListChanged -= OnPlayerListChanged;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer)
            return; // Only handle collisions on the server

        if (collision.gameObject.CompareTag("Book"))
        {
            if (collision.gameObject.TryGetComponent<NetworkObject>(out var bookObject))
            {
                if (bookObject.IsSpawned)
                {
                    tailManager.ModifyTailLength(1);
                    bookObject.Despawn();
                }
            }
        }
        else if (collision.gameObject.CompareTag("Fruit"))
        {
            //refill boost
            Boost.Value += 25;
            collision.gameObject.GetComponent<NetworkObject>().Despawn();
        }
        else if (collision.collider.CompareTag("Player"))
        {
            // collision.otherCollider is THIS player's collider that was involved.
            // If it's not the head, our tail was hit — the OTHER player dies, not us.
            if (collision.otherCollider != headCollider)
                return;
            collisionCooldown = 0.3f;
            // Hit another snake's tail — eliminate this player
            IsAlive.Value = false;
            tailManager.SpawnBooksOnDeath();

            GameObject killer = collision.collider.transform.parent.gameObject;
            ulong killerClientId = killer.GetComponent<NetworkObject>().NetworkObjectId;

            FollowKillerRpc(killerClientId, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!IsServer)
            return; // Only handle collisions on the server

        if (collision.gameObject.CompareTag("Obstacle") || collision.gameObject.CompareTag("Door"))
        {
            if (collisionCooldown > 0f)
                return; // Ignore collisions during cooldown
            collisionCooldown = 0.3f;
            tailManager.ModifyTailLength(-2);
            Vector2 collisionNormal = collision.contacts[0].normal;
            Vector2 bounceDirection = Vector2.Reflect(transform.up, collisionNormal).normalized;
            transform.rotation = Quaternion.LookRotation(Vector3.forward, bounceDirection);
        }
    }

    private void FixedUpdate()
    {
        if (collisionCooldown > 0f)
            collisionCooldown -= Time.fixedDeltaTime;

        if (IsOwner && IsAlive.Value)
        {
            HandleInput();
        }

        // Apply movement on server with authority
        if (IsServer && IsAlive.Value)
        {
            ApplyMovement(serverHorizontalInput, serverIsBoosting);
        }
        // Client-side prediction for owner only
        else if (IsOwner && IsAlive.Value)
        {
            ApplyMovement(predictedHorizontalInput, predictedIsBoosting);
        }
    }

    private void HandleInput()
    {
        float horizontalInput = moveAction.ReadValue<Vector2>().x;
        bool boostInput = boostAction.IsPressed();

        // Client-side prediction
        predictedHorizontalInput = horizontalInput;
        predictedIsBoosting = boostInput;

        // Rate limiting - only send to server at specified intervals
        if (Time.time - lastInputSendTime >= inputSendRate)
        {
            SendInputToServerRpc(horizontalInput, boostInput);
            lastInputSendTime = Time.time;
        }
    }

    private void ApplyMovement(float horizontalInput, bool isBoosting)
    {
        float currentSpeed = baseSpeed;

        // Only deplete boost on server
        if (IsServer && isBoosting && Boost.Value > 0)
        {
            currentSpeed = baseSpeed * boostMultiplier;

            // Deplete boost based on time
            boostTimer += Time.fixedDeltaTime;
            if (boostTimer >= 1f / boostDepletionRate)
            {
                Boost.Value = Mathf.Max(0, Boost.Value - 1);
                boostTimer = 0f;
            }
        }

        // Apply movement
        transform.Translate(currentSpeed * Time.fixedDeltaTime * Vector2.up, Space.Self);
        transform.Rotate(-horizontalInput * rotationSpeed * Time.fixedDeltaTime * Vector3.forward);
    }

    private void OnIsAliveChanged(bool previousValue, bool newValue)
    {
        // only care about changes to false (death)
        if (newValue)
            return;

        // Hide the player object on all clients when they die
        gameObject.SetActive(false);

        if (IsOwner)
        {
            moveAction.Disable();
            boostAction.Disable();
        }

        // Server marks the player as dead in the shared leaderboard record
        if (IsServer && TillerioGameController.Instance != null)
            TillerioGameController.Instance.UpdatePlayerAlive(OwnerClientId, false);
    }

    private void OnPlayerListChanged(NetworkListEvent<PlayerData> _) => ApplyPlayerLooks();

    private void ApplyPlayerLooks()
    {
        if (iconRenderer == null || TillerioGameController.Instance == null)
            return;
        var list = TillerioGameController.Instance.PlayerDataList;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].NetworkClientId != OwnerClientId)
                continue;
            var sprites = TillerioGameController.Instance.PlayerSprites;
            int idx = list[i].SpriteIndex;
            if (idx >= 0 && idx < sprites.Count)
                iconRenderer.sprite = sprites[idx];
            return;
        }
    }

    // ------------ SERVER RPC -------------

    [ServerRpc]
    private void SendInputToServerRpc(
        float horizontalInput,
        bool boostInput,
        ServerRpcParams rpcParams = default
    )
    {
        // Security: Verify ownership
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning(
                $"Client {rpcParams.Receive.SenderClientId} tried to control player owned by {OwnerClientId}"
            );
            return;
        }

        // Security: Validate input range
        if (Mathf.Abs(horizontalInput) > maxInputValue)
        {
            Debug.LogWarning(
                $"Client {rpcParams.Receive.SenderClientId} sent invalid input: {horizontalInput}"
            );
            horizontalInput = Mathf.Clamp(horizontalInput, -1f, 1f);
        }

        // Security: Can't boost if no boost remaining
        if (boostInput && Boost.Value <= 0)
        {
            boostInput = false;
        }

        // Update server-authoritative state
        serverHorizontalInput = horizontalInput;
        serverIsBoosting = boostInput;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void FollowKillerRpc(ulong killerNetObjId, RpcParams rpcParams = default)
    {
        if (
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                killerNetObjId,
                out var netObj
            )
        )
        {
            var killer = netObj.GetComponent<Player>();
            if (killer != null && killer.IsAlive.Value)
            {
                CameraFollow.Instance?.FollowPlayer(killer);
                return;
            }
        }
        CameraFollow.Instance?.FindNextAlivePlayer();
    }
}
