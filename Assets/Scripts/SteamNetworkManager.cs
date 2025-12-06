using UnityEngine;
using Mirror;
using Steamworks;
using System.Collections;
using System.Collections.Generic;

public class SteamNetworkManager : NetworkManager
{
    [Header("Player Prefabs")]
    public GameObject xrNetworkPlayerPrefab;

    [Header("Spawn Settings")]
    public Vector3 spawnCenter = new Vector3(336f, 0.25f, -606f);
    public float spawnRadius = 1.5f;

    private Callback<LobbyEnter_t> lobbyEnterCallback;
    private Callback<LobbyCreated_t> lobbyCreatedCallback;
    private Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequestedCallback;

    private CSteamID currentLobbyID;
    public CSteamID CurrentLobbyID => currentLobbyID;

    private LobbyUI lobbyUI;
    private bool gameStarted = false;
    private bool lobbyUIDisabled = false;

    private Dictionary<int, int> connectionSpawnIndex = new Dictionary<int, int>();
    private int nextSpawnIndex = 0;

    public override void Awake()
    {
        base.Awake();
        autoCreatePlayer = false;
        playerPrefab = null;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (SteamInitializer.Initialized) HookCallbacks();
        else SteamInitializer.OnSteamInitialized += HookCallbacks;
    }

    private void HookCallbacks()
    {
        if (lobbyEnterCallback != null) return;

        lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);

        SteamInitializer.OnSteamInitialized -= HookCallbacks;
        Debug.Log("[SteamNetworkManager] Steam callbacks hooked.");
    }

    private new void Start()
    {
        base.Start();
        lobbyUI = FindFirstObjectByType<LobbyUI>();
        NetworkClient.RegisterHandler<StatusUpdateMessage>(OnStatusUpdateMessage);

        if (xrNetworkPlayerPrefab == null)
        {
            Debug.LogError("[SteamNetworkManager] xrNetworkPlayerPrefab not assigned! Please assign it in the Inspector!");
        }
    }

    private void OnStatusUpdateMessage(StatusUpdateMessage msg)
    {
        Debug.Log($"[SteamNetworkManager] Received status update: {msg.status}");
        lobbyUI?.UpdateStatus(msg.status);
    }

    public void HostLobby()
    {
        if (!SteamInitializer.Initialized)
        {
            Debug.LogWarning("[SteamNetworkManager] Steam not initialized!");
            lobbyUI?.UpdateStatus("Error: Steam not initialized!");
            return;
        }

        lobbyUI?.UpdateStatus("Creating lobby...");
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxConnections);
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult == EResult.k_EResultOK)
        {
            currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
            Debug.Log($"[SteamNetworkManager] Lobby created: {currentLobbyID}");
            lobbyUI?.UpdateStatus("Lobby created! Waiting for players...");

            try
            {
                SteamMatchmaking.SetLobbyData(currentLobbyID, "name",
                    SteamFriends.GetPersonaName() + "'s Lobby");
            }
            catch { }

            StartCoroutine(StartHostDelayed());
        }
        else
        {
            Debug.LogError($"[SteamNetworkManager] Failed to create lobby: {callback.m_eResult}");
            lobbyUI?.UpdateStatus($"Failed to create lobby: {callback.m_eResult}");
        }
    }

    private IEnumerator StartHostDelayed()
    {
        yield return null;
        StartHost();
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        lobbyUI?.UpdateStatus("Joining friend's lobby...");
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    private void OnLobbyEnter(LobbyEnter_t callback)
    {
        currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);

        if (!NetworkServer.active)
        {
            lobbyUI?.UpdateStatus("Joined lobby! Connecting to host...");
            CSteamID hostSteamID = SteamMatchmaking.GetLobbyOwner(currentLobbyID);
            networkAddress = hostSteamID.ToString();
            StartCoroutine(StartClientDelayed());
        }
        else
        {
            lobbyUI?.UpdateStatus("Lobby ready! Waiting for players...");
        }
    }

    private IEnumerator StartClientDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        StartClient();
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        lobbyUI?.UpdateStatus("Connected to host!");
        Debug.Log("[SteamNetworkManager] Client connected successfully");
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);

        int totalPlayers = NetworkServer.connections.Count;
        lobbyUI?.UpdateStatus($"Player joined! ({totalPlayers} total)");

        if (!connectionSpawnIndex.ContainsKey(conn.connectionId))
        {
            connectionSpawnIndex[conn.connectionId] = nextSpawnIndex++;
            Debug.Log($"[SteamNetworkManager] Assigned spawn index {connectionSpawnIndex[conn.connectionId]} to connection {conn.connectionId}");
        }

        // If game already started, spawn immediately for late joiners
        if (gameStarted)
        {
            Debug.Log($"[SteamNetworkManager] Late joiner detected, spawning immediately for connection {conn.connectionId}");
            SpawnPlayerForConnection(conn);
        }
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        lobbyUI?.UpdateStatus("Disconnected from host");

        if (currentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(currentLobbyID);
            currentLobbyID = new CSteamID();
        }

        gameStarted = false;
        lobbyUIDisabled = false;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        lobbyUI?.UpdateStatus("Server stopped");

        if (currentLobbyID.IsValid())
            SteamMatchmaking.LeaveLobby(currentLobbyID);

        gameStarted = false;
        lobbyUIDisabled = false;
        connectionSpawnIndex.Clear();
        nextSpawnIndex = 0;
    }

    public void StartGame()
    {
        if (!NetworkServer.active)
        {
            lobbyUI?.UpdateStatus("Error: Only host can start the game");
            return;
        }

        if (gameStarted)
        {
            Debug.LogWarning("[SteamNetworkManager] Game already started!");
            return;
        }

        gameStarted = true;
        int connectionCount = NetworkServer.connections.Count;
        Debug.Log($"[SteamNetworkManager] Starting game with {connectionCount} players");

        // Notify all clients that game is starting
        SendStatusToAllClients("Game starting...");

        // Tell all clients to be on player camera
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn.identity != null)
            {
                XRNetworkPlayer player = conn.identity.GetComponent<XRNetworkPlayer>();
                if (player != null)
                {
                    player.TargetActivateCamera(conn);
                }
            }
        }

        StartCoroutine(SpawnAllPlayersDelayed());
    }

    private IEnumerator SpawnAllPlayersDelayed()
    {
        yield return new WaitForSeconds(0.1f);

        int spawnedCount = 0;
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            Debug.Log($"[SteamNetworkManager] Spawning player {spawnedCount + 1} for connection {conn.connectionId}");
            SpawnPlayerForConnection(conn);
            spawnedCount++;
        }

        Debug.Log($"[SteamNetworkManager] Finished spawning {spawnedCount} players");
    }

    private void SendStatusToAllClients(string status)
    {
        lobbyUI?.UpdateStatus(status);

        if (NetworkServer.active)
        {
            NetworkServer.SendToAll(new StatusUpdateMessage { status = status });
        }
    }

    private void SpawnPlayerForConnection(NetworkConnectionToClient conn)
    {
        if (xrNetworkPlayerPrefab == null)
        {
            Debug.LogError("[SteamNetworkManager] xrNetworkPlayerPrefab is NULL! Cannot spawn player!");
            return;
        }

        if (conn.identity != null)
        {
            Debug.LogWarning($"[SteamNetworkManager] Connection {conn.connectionId} already has a player, skipping spawn");
            return;
        }

        int index = connectionSpawnIndex.TryGetValue(conn.connectionId, out int i) ? i : nextSpawnIndex++;
        float angleRad = (index * 45f) * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(angleRad) * spawnRadius, 0f, Mathf.Sin(angleRad) * spawnRadius);
        Vector3 spawnPos = spawnCenter + offset;

        Debug.Log($"[SteamNetworkManager] Spawning player at {spawnPos} (index {index}) for connection {conn.connectionId}");

        GameObject playerObj = Instantiate(xrNetworkPlayerPrefab, spawnPos, Quaternion.identity);
        if (playerObj == null)
        {
            Debug.LogError("[SteamNetworkManager] Failed to instantiate player prefab!");
            return;
        }

        NetworkServer.AddPlayerForConnection(conn, playerObj);
        Debug.Log($"[SteamNetworkManager] Player spawned successfully for connection {conn.connectionId}");
    }
}

public struct StatusUpdateMessage : NetworkMessage
{
    public string status;
}
