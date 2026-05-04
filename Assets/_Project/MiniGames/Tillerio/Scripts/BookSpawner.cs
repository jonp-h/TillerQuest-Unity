using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class BookSpawner : NetworkBehaviour
{
    [SerializeField]
    private GameObject bookPrefab;

    [SerializeField]
    private GameObject foodPrefab;

    [SerializeField]
    private float upperSpawnInterval = 5f;

    [SerializeField]
    private float lowerSpawnInterval = 1f;

    [SerializeField]
    private List<Transform> spawnAreas;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }
        TillerioGameController.Instance.CurrentGameState.OnValueChanged += OnGameStateChanged;
        SpawnBook();
        SpawnFruit();
    }

    private void OnGameStateChanged(
        TillerioGameController.GameState oldState,
        TillerioGameController.GameState newState
    )
    {
        if (newState == TillerioGameController.GameState.Playing)
        {
            SpawnBook();
            SpawnFruit();
        }
    }

    private void SpawnBook()
    {
        if (spawnAreas.Count == 0)
        {
            Debug.LogWarning("No spawn areas defined for food.");
            return;
        }

        // Choose a random spawn area
        Transform spawnArea = spawnAreas[Random.Range(0, spawnAreas.Count)];

        // Get the bounds of the spawn area
        Collider2D areaCollider = spawnArea.GetComponent<Collider2D>();
        if (areaCollider == null)
        {
            Debug.LogWarning($"Spawn area {spawnArea.name} does not have a Collider2D component.");
            return;
        }

        Bounds bounds = areaCollider.bounds;

        // Generate a random position within the bounds
        Vector2 randomPosition = new Vector2(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y)
        );

        // Instantiate the book prefab at the random position
        GameObject book = Instantiate(bookPrefab, randomPosition, Quaternion.identity);
        NetworkObject netObj = book.GetComponent<NetworkObject>();

        netObj.Spawn();
        if (
            TillerioGameController.Instance != null
            && TillerioGameController.Instance.CurrentGameState.Value
                == TillerioGameController.GameState.Playing
        )
        {
            Invoke(nameof(SpawnBook), Random.Range(lowerSpawnInterval, upperSpawnInterval));
        }
    }

    private void SpawnFruit()
    {
        if (spawnAreas.Count == 0)
        {
            Debug.LogWarning("No spawn areas defined for food.");
            return;
        }

        // Choose a random spawn area
        Transform spawnArea = spawnAreas[Random.Range(0, spawnAreas.Count)];

        // Get the bounds of the spawn area
        Collider2D areaCollider = spawnArea.GetComponent<Collider2D>();
        if (areaCollider == null)
        {
            Debug.LogWarning($"Spawn area {spawnArea.name} does not have a Collider2D component.");
            return;
        }

        Bounds bounds = areaCollider.bounds;

        // Generate a random position within the bounds
        Vector2 randomPosition = new Vector2(
            Random.Range(bounds.min.x, bounds.max.x),
            Random.Range(bounds.min.y, bounds.max.y)
        );

        // Instantiate the fruit prefab at the random position
        GameObject fruit = Instantiate(foodPrefab, randomPosition, Quaternion.identity);
        NetworkObject netObj = fruit.GetComponent<NetworkObject>();

        netObj.Spawn();

        if (
            TillerioGameController.Instance != null
            && TillerioGameController.Instance.CurrentGameState.Value
                == TillerioGameController.GameState.Playing
        )
        {
            Invoke(
                nameof(SpawnFruit),
                Random.Range(lowerSpawnInterval * 3, upperSpawnInterval * 3)
            );
        }
    }
}
