using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TailManager : NetworkBehaviour
{
    /// <summary>
    /// Reference to the tail prefab that will be instantiated for each segment
    /// </summary>
    [SerializeField]
    private Transform tailPrefab;

    /// <summary>
    /// Reference to the book prefab that will be instantiated on death
    /// </summary>
    [SerializeField]
    private GameObject bookPrefab;

    /// <summary>
    /// The distance between each position point in the tail's path.
    /// Smaller values create a smoother, more detailed tail path.
    /// Should be the same size as the collider.
    /// </summary>
    [SerializeField]
    private float size = 0.7f;

    private Color tailColor;

    /// <summary>
    /// List of all visible tail segment transforms that make up the tail
    /// </summary>
    [SerializeField]
    private List<Transform> tailSegments = new List<Transform>();

    /// <summary>
    /// List of positions that define the path the tail follows.
    /// New positions are added at the front as the player moves.
    /// </summary>
    [SerializeField]
    private List<Vector2> positions = new List<Vector2>();

    // <summary>
    // Network variable to track the length of the tail across the network.
    // This allows all clients to know how many segments the tail should have.
    // The server is responsible for updating this variable when segments are added or removed.
    // </summary>
    public NetworkVariable<int> tailLength = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        tailLength.OnValueChanged += OnTailLengthChanged;
        InitialiseColor();

        // Late-join clients get their current tail length synced immediately
        if (tailLength.Value > 0)
        {
            OnTailLengthChanged(0, tailLength.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        tailLength.OnValueChanged -= OnTailLengthChanged;
    }

    private void Update()
    {
        if (positions.Count < 2)
        {
            return; // Not enough position points to update the tail
        }

        // Calculate how far the player has moved from the first position point
        float distance = ((Vector2)transform.position - positions[0]).magnitude;

        // If the player has moved far enough, add a new position point
        if (distance > size)
        {
            // Calculate the direction from the first position toward the player
            Vector2 direction = ((Vector2)transform.position - positions[0]).normalized;

            // Insert a new position point at the front of the list, 'size' distance from the previous front
            positions.Insert(0, positions[0] + direction * size);

            // Remove the oldest position from the end to maintain a constant tail length
            positions.RemoveAt(positions.Count - 1);

            // Adjust distance for the remaining movement that wasn't accounted for
            distance -= size;
        }

        // Update each tail segment's position by interpolating between position points
        for (int i = 0; i < tailSegments.Count; i++)
        {
            // Smoothly interpolate each segment between two position points
            // based on how far the player has moved since the last position update
            tailSegments[i].position = Vector2.Lerp(
                positions[i + 1], // The older position point
                positions[i], // The newer position point
                distance / size // Interpolation factor based on player movement
            );
        }
    }

    public void ModifyTailLength(int delta)
    {
        // Only the server should modify the tail length to ensure consistency across clients
        if (IsServer)
        {
            tailLength.Value = Mathf.Max(0, tailLength.Value + delta); // Ensure tail length doesn't go negative
        }
    }

    public void OnTailLengthChanged(int oldLength, int newLength)
    {
        int diff = newLength - oldLength;
        if (diff > 0)
        {
            // Tail length increased, add new segments
            for (int i = 0; i < diff; i++)
            {
                AddTailSegmentLocal();
            }
        }
        else if (diff < 0)
        {
            // Tail length decreased, remove segments
            LoseTailSegmentsLocal(false, Mathf.Abs(diff));
        }

        if (IsServer && TillerioGameController.Instance != null)
        {
            // Update the player's score based on their tail length (e.g., 1 point per segment)
            Debug.Log(
                $"[TailManager] Updating score for OwnerClientId={OwnerClientId}, newLength={newLength}. PlayerDataList count={TillerioGameController.Instance.PlayerDataList.Count}"
            );
            TillerioGameController.Instance.UpdatePlayerScore(OwnerClientId, newLength);
        }
    }

    public void AddTailSegmentLocal()
    {
        Vector2 startingTailPosition =
            positions.Count > 0 ? positions[^1] : (Vector2)transform.position;
        // Ensure positions list is long enough before adding a segment
        while (positions.Count <= tailSegments.Count + 1)
            positions.Add(startingTailPosition);

        Transform newTail = Instantiate(
            tailPrefab,
            startingTailPosition,
            Quaternion.identity,
            transform
        );

        newTail.GetComponent<SpriteRenderer>().color = tailColor;

        // Ignore collision between this player's head and its own tail segment
        Physics2D.IgnoreCollision(
            gameObject.GetComponent<Collider2D>(),
            newTail.GetComponent<Collider2D>()
        );

        tailSegments.Add(newTail);
    }

    public void LoseTailSegmentsLocal(bool loseEntireTail, int count)
    {
        if (loseEntireTail)
        {
            count = tailSegments.Count;
        }
        for (int i = 0; i < count && tailSegments.Count > 0; i++)
        {
            Transform segment = tailSegments[^1];
            tailSegments.RemoveAt(tailSegments.Count - 1);
            Destroy(segment.gameObject);
        }
    }

    public void SpawnBooksOnDeath()
    {
        if (!IsServer)
            return; // Only spawn books on the server

        var spawnPositions = new List<Vector2>(positions.GetRange(0, tailSegments.Count));

        foreach (var position in spawnPositions)
        {
            var book = Instantiate(bookPrefab, position, Quaternion.identity);
            book.GetComponent<NetworkObject>().Spawn();
        }
    }

    public void InitialiseColor()
    {
        if (TillerioGameController.Instance == null)
            return;
        var list = TillerioGameController.Instance.PlayerDataList;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].NetworkClientId != OwnerClientId)
                continue;
            tailColor = list[i].TailColor;
            return;
        }
    }
}
