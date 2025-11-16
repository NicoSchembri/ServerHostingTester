using UnityEngine;
using Mirror;
using Steamworks;
using System.Collections;

public class SteamNetworkManager : NetworkManager
{
    [Header("Player Prefabs")]
    public GameObject xrNetworkPlayerPrefab;

    private Callback<LobbyEnter_t> lobbyEnterCallback;
    private Callback<LobbyCreated_t> lobbyCreatedCallback;
    private Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequestedCallback;

    private CSteamID currentLobbyID;
    public CSteamID CurrentLobbyID => currentLobbyID;

    private LobbyUI lobbyUI;
    private bool gameStarted = false;

    public override void Awake()
    {
        base.Awake();
        autoCreatePlayer = false; 
        playerPrefab = null; 
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (SteamInitializer.Initialized)
        {
            HookCallbacks();
        }
        else
        {
            SteamInitializer.OnSteamInitialized += HookCallbacks;
        }
    }

    private void HookCallbacks()
    {
        lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);

        SteamInitializer.OnSteamInitialized -= HookCallbacks;
        Debug.Log("[SteamNetworkManager] Steam callbacks hooked.");
    }

    private new void Start()
    {
        lobbyUI = FindFirstObjectByType<LobbyUI>();
    }

    public void HostLobby()
    {
        if (!SteamInitializer.Initialized)
        {
            Debug.LogWarning("[SteamNetworkManager] Steam not initialized! Cannot host.");
            lobbyUI?.UpdateStatus("Error: Steam not initialized!");
            return;
        }

        Debug.Log("[SteamNetworkManager] Creating Steam lobby...");
        lobbyUI?.UpdateStatus("Creating lobby...");
        SteamAPICall_t handle = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxConnections);
    }

    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult == EResult.k_EResultOK)
        {
            currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
            Debug.Log($"[SteamNetworkManager] Lobby created successfully: {currentLobbyID}");
            lobbyUI?.UpdateStatus("Lobby created! Waiting for players...");

            // Set lobby data for visibility
            SteamMatchmaking.SetLobbyData(currentLobbyID, "name", SteamFriends.GetPersonaName() + "'s Lobby");

            DebugSteamStatus();

            // Start Mirror host - FizzySteamworks handles the connection automatically
            StartCoroutine(StartHostDelayed());
        }
        else
        {
            Debug.LogError($"[SteamNetworkManager] Failed to create lobby: {callback.m_eResult}");
            lobbyUI?.UpdateStatus($"Failed to create lobby: {callback.m_eResult}");

            if (lobbyUI != null && lobbyUI.createLobbyButton != null)
                lobbyUI.createLobbyButton.interactable = true;
        }
    }

    private IEnumerator StartHostDelayed()
    {
        yield return null;
        Debug.Log($"[SteamNetworkManager] Starting Mirror host...");
        StartHost();
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        Debug.Log($"[SteamNetworkManager] Received join request for lobby {callback.m_steamIDLobby}");
        lobbyUI?.UpdateStatus("Joining friend's lobby...");
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    private void OnLobbyEnter(LobbyEnter_t callback)
    {
        currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        Debug.Log($"[SteamNetworkManager] Entered lobby {currentLobbyID}");

        DebugSteamStatus();

        // If we're not the host, connect as client
        if (!NetworkServer.active)
        {
            lobbyUI?.UpdateStatus("Joined lobby! Connecting to host...");

            // Get the host's Steam ID (lobby owner)
            CSteamID hostSteamID = SteamMatchmaking.GetLobbyOwner(currentLobbyID);
            Debug.Log($"[SteamNetworkManager] Connecting to host: {hostSteamID}");

            // FizzySteamworks uses the Steam ID directly
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
        Debug.Log($"[SteamNetworkManager] Starting Mirror client...");
        StartClient();
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("[SteamNetworkManager] Client connected to host!");
        lobbyUI?.UpdateStatus("Connected to host!");
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        Debug.Log($"[SteamNetworkManager] Player connected: {conn}");
        lobbyUI?.UpdateStatus($"Player joined! ({NetworkServer.connections.Count} total)");

        // If game already started, spawn player immediately
        if (gameStarted)
        {
            SpawnPlayerForConnection(conn);
        }
    }

    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        Debug.LogError($"[SteamNetworkManager] Client connection error: {error} - {reason}");
        lobbyUI?.UpdateStatus($"Connection failed: {reason}");
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("[SteamNetworkManager] Client disconnected");

        if (lobbyUI != null)
        {
            lobbyUI.gameObject.SetActive(true);
            lobbyUI.UpdateStatus("Disconnected from host");
        }

        if (currentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(currentLobbyID);
            currentLobbyID.Clear();
        }

        gameStarted = false;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("[SteamNetworkManager] Server stopped");
        lobbyUI?.UpdateStatus("Server stopped");

        if (currentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(currentLobbyID);
            currentLobbyID.Clear();
        }

        gameStarted = false;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log("[SteamNetworkManager] Client stopped");

        if (lobbyUI != null && !gameStarted)
        {
            lobbyUI.UpdateStatus("Disconnected from host");
        }
    }

    public void StartGame()
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("[SteamNetworkManager] Only the host can start the game!");
            lobbyUI?.UpdateStatus("Error: Only host can start the game");
            return;
        }

        if (gameStarted)
        {
            Debug.LogWarning("[SteamNetworkManager] Game already started!");
            return;
        }

        Debug.Log("[SteamNetworkManager] Starting game for all players...");
        gameStarted = true;

        // Spawn XRNetworkPlayer for each connected player
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            SpawnPlayerForConnection(conn);
        }
    }

    private void SpawnPlayerForConnection(NetworkConnectionToClient conn)
    {
        // Remove existing player object if any
        if (conn.identity != null)
        {
            NetworkServer.Destroy(conn.identity.gameObject);
        }

        // Base spawn position
        Vector3 basePos = new Vector3(336f, 0.25f, -606f);

        int playerIndex = NetworkServer.connections.Count; 
        float radius = 1.5f; 
        float angle = (playerIndex - 1) * 45f; 
        float rad = angle * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
        Vector3 spawnPos = basePos + offset;
        Quaternion spawnRot = Quaternion.identity;

        // Instantiate player
        GameObject playerObj = Instantiate(xrNetworkPlayerPrefab, spawnPos, spawnRot);
        NetworkServer.AddPlayerForConnection(conn, playerObj);

        Debug.Log($"[SteamNetworkManager] Spawned XRNetworkPlayer at {spawnPos} for connection {conn.connectionId}");
    }

    private void DebugSteamStatus()
    {
        if (!SteamInitializer.Initialized)
        {
            Debug.LogError("[SteamNetworkManager] Steam not initialized!");
            return;
        }

        Debug.Log($"[SteamNetworkManager] === STEAM STATUS ===");
        Debug.Log($"[SteamNetworkManager] Steam ID: {SteamUser.GetSteamID()}");
        Debug.Log($"[SteamNetworkManager] Persona: {SteamFriends.GetPersonaName()}");
        Debug.Log($"[SteamNetworkManager] Logged On: {SteamUser.BLoggedOn()}");
        Debug.Log($"[SteamNetworkManager] App ID: {SteamUtils.GetAppID()}");

        if (currentLobbyID.IsValid())
        {
            Debug.Log($"[SteamNetworkManager] Current Lobby: {currentLobbyID}");
            Debug.Log($"[SteamNetworkManager] Lobby Member Count: {SteamMatchmaking.GetNumLobbyMembers(currentLobbyID)}");
            CSteamID hostID = SteamMatchmaking.GetLobbyOwner(currentLobbyID);
            Debug.Log($"[SteamNetworkManager] Lobby Owner: {hostID}");
            string lobbyName = SteamMatchmaking.GetLobbyData(currentLobbyID, "name");
            Debug.Log($"[SteamNetworkManager] Lobby Name: '{lobbyName}'");
        }
        else
        {
            Debug.Log("[SteamNetworkManager] Not in a lobby");
        }
        Debug.Log($"[SteamNetworkManager] === END STATUS ===");
    }

    private void OnDisable()
    {
        SteamInitializer.OnSteamInitialized -= HookCallbacks;
    }
}