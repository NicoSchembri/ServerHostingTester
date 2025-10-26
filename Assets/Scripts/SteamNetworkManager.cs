using UnityEngine;
using Mirror;
using Steamworks;
using System.Collections;

public class SteamNetworkManager : NetworkManager
{
    [Header("Scenes")]
    public string mainSceneName = "MainScene";

    private Callback<LobbyEnter_t> lobbyEnterCallback;
    private Callback<LobbyCreated_t> lobbyCreatedCallback;
    private Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequestedCallback;

    private CSteamID currentLobbyID;
    public CSteamID CurrentLobbyID => currentLobbyID;

    private LobbyUI lobbyUI;

    public override void Awake()
    {
        base.Awake();
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

    // Called when you click "Create Lobby"
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

    // Called when Steam lobby is successfully created
    private void OnLobbyCreated(LobbyCreated_t callback)
    {
        if (callback.m_eResult == EResult.k_EResultOK)
        {
            currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
            Debug.Log($"[SteamNetworkManager] Lobby created successfully: {currentLobbyID}");
            lobbyUI?.UpdateStatus("Lobby created! Waiting for players...");

            // Set lobby data BEFORE starting host
            SteamMatchmaking.SetLobbyData(currentLobbyID, "HostAddress", SteamUser.GetSteamID().ToString());
            SteamMatchmaking.SetLobbyData(currentLobbyID, "name", SteamFriends.GetPersonaName() + "'s Lobby");

            // DEBUG: Check lobby data immediately after setting it
            DebugSteamStatus();

            // Wait a frame to ensure lobby data is propagated to Steam
            StartCoroutine(StartHostDelayed());
        }
        else
        {
            Debug.LogError($"[SteamNetworkManager] Failed to create lobby: {callback.m_eResult}");
            lobbyUI?.UpdateStatus($"Failed to create lobby: {callback.m_eResult}");

            // Re-enable the create button on failure
            if (lobbyUI != null && lobbyUI.createLobbyButton != null)
                lobbyUI.createLobbyButton.interactable = true;
        }
    }

    private IEnumerator StartHostDelayed()
    {
        yield return null; // Wait one frame for lobby data to propagate through Steam
        Debug.Log($"[SteamNetworkManager] Starting host after lobby data set...");
        StartHost();
    }

    // Called when Steam notifies us that we should join a friend's lobby (Steam Overlay "Join Game")
    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
    {
        Debug.Log($"[SteamNetworkManager] Received join request for lobby {callback.m_steamIDLobby}");
        lobbyUI?.UpdateStatus("Joining friend's lobby...");
        SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
    }

    // Called once the player successfully joins the Steam lobby
    private void OnLobbyEnter(LobbyEnter_t callback)
    {
        currentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        Debug.Log($"[SteamNetworkManager] Entered lobby {currentLobbyID}");

        // DEBUG: Check status immediately upon entering lobby
        DebugSteamStatus();

        string hostAddress = SteamMatchmaking.GetLobbyData(currentLobbyID, "HostAddress");

        // Validate host address
        if (string.IsNullOrEmpty(hostAddress))
        {
            Debug.LogError("[SteamNetworkManager] No HostAddress found in lobby data!");
            lobbyUI?.UpdateStatus("Error: Could not find host address");
            return;
        }

        lobbyUI?.UpdateStatus("Joined lobby! Connecting to host...");
        Debug.Log($"[SteamNetworkManager] Connecting to host: {hostAddress}");

        // Only auto-join as client if we're not the host
        if (!NetworkServer.active)
        {
            networkAddress = hostAddress;

            // Add a small delay to ensure everything is ready
            StartCoroutine(StartClientDelayed());
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
        Debug.Log("[SteamNetworkManager] Client connected to host!");
        lobbyUI?.UpdateStatus("Connected to host!");
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
        lobbyUI?.UpdateStatus("Disconnected from host");

        // Leave Steam lobby on disconnect
        if (currentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(currentLobbyID);
            currentLobbyID.Clear();
        }
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        Debug.Log($"[SteamNetworkManager] Player connected: {conn}");
        lobbyUI?.UpdateStatus("Player joined lobby!");
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log("[SteamNetworkManager] Server stopped");
        lobbyUI?.UpdateStatus("Server stopped");

        // Leave Steam lobby when server stops
        if (currentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(currentLobbyID);
            currentLobbyID.Clear();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log("[SteamNetworkManager] Client stopped");
        lobbyUI?.UpdateStatus("Disconnected from host");
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

        // Check if we're in a lobby
        if (currentLobbyID.IsValid())
        {
            Debug.Log($"[SteamNetworkManager] Current Lobby: {currentLobbyID}");
            Debug.Log($"[SteamNetworkManager] Lobby Member Count: {SteamMatchmaking.GetNumLobbyMembers(currentLobbyID)}");
            string hostAddress = SteamMatchmaking.GetLobbyData(currentLobbyID, "HostAddress");
            Debug.Log($"[SteamNetworkManager] Lobby Host Address: '{hostAddress}'");
            Debug.Log($"[SteamNetworkManager] Host Address Is Null/Empty: {string.IsNullOrEmpty(hostAddress)}");
        }
        else
        {
            Debug.Log("[SteamNetworkManager] Not in a lobby");
        }
        Debug.Log($"[SteamNetworkManager] === END STATUS ===");
    }
}