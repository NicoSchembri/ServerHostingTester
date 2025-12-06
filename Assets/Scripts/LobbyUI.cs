using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using System.Collections;
using Steamworks;
using Mirror;

public class LobbyUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Button createLobbyButton;
    public Button startGameButton;
    public TMP_Text statusText;
    public TMP_Text instructionsText;
    public TMP_Text lobbyMembersText;

    [Header("Cameras")]
    public Camera lobbyCamera;

    private SteamNetworkManager networkManager;
    private Coroutine memberUpdateCoroutine;
    private Coroutine startButtonCoroutine;
    private bool isInitialized = false;

    private void Start()
    {
        StartCoroutine(InitializeUI());
    }

    private IEnumerator InitializeUI()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForSeconds(0.1f);

        SafeSetText(statusText, "Initializing...");
        SafeSetText(instructionsText, "");
        SafeSetText(lobbyMembersText, "");

        while (!SteamInitializer.Initialized)
        {
            UpdateStatus("Waiting for Steam...");
            yield return new WaitForSeconds(0.5f);
        }

        networkManager = FindFirstObjectByType<SteamNetworkManager>();
        if (networkManager == null)
        {
            UpdateStatus("Error: NetworkManager missing!");
            yield break;
        }

        createLobbyButton?.onClick.AddListener(OnCreateLobbyClicked);
        createLobbyButton.interactable = true;

        startGameButton?.onClick.AddListener(OnStartGameClicked);
        startGameButton.interactable = false;

        instructionsText.text =
            "To join a friend's lobby:\n" +
            "1. Press Shift+Tab\n" +
            "2. Right-click friend → Join Game";

        isInitialized = true;
        memberUpdateCoroutine = StartCoroutine(UpdateLobbyMembersRoutine());
        startButtonCoroutine = StartCoroutine(UpdateStartButtonRoutine());

        UpdateStatus("Ready - Create a lobby or join via Steam overlay");
    }

    private void OnCreateLobbyClicked()
    {
        UpdateStatus("Creating lobby...");
        createLobbyButton.interactable = false;

        networkManager = networkManager ?? FindFirstObjectByType<SteamNetworkManager>();
        networkManager?.HostLobby();
    }

    private void OnStartGameClicked()
    {
        if (networkManager == null)
            return;

        if (!NetworkServer.active)
        {
            UpdateStatus("Only host can start the game!");
            return;
        }

        UpdateStatus("Starting game...");
        networkManager.StartGame();
    }

    private IEnumerator UpdateLobbyMembersRoutine()
    {
        while (true)
        {
            if (isInitialized)
                UpdateLobbyMembers();

            yield return new WaitForSeconds(2f);
        }
    }

    private void UpdateLobbyMembers()
    {
        if (networkManager == null || lobbyMembersText == null)
            return;

        CSteamID lobbyId = networkManager.CurrentLobbyID;
        if (!lobbyId.IsValid())
        {
            SafeSetText(lobbyMembersText, "Not in a lobby");
            return;
        }

        int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
        StringBuilder sb = new StringBuilder();

        sb.AppendLine($"Lobby Members ({memberCount}):");

        for (int i = 0; i < memberCount; i++)
        {
            CSteamID memberID = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
            string name = "(unknown)";

            try { name = SteamFriends.GetFriendPersonaName(memberID); }
            catch { }

            sb.AppendLine($"- {name}");
        }

        SafeSetText(lobbyMembersText, sb.ToString());
    }

    private IEnumerator UpdateStartButtonRoutine()
    {
        while (true)
        {
            if (startGameButton != null)
                startGameButton.interactable = NetworkServer.active;

            yield return new WaitForSeconds(0.5f);
        }
    }

    public void UpdateStatus(string message)
    {
        SafeSetText(statusText, message);
        Debug.Log("[LobbyUI] " + message);
    }

    public void ShowGameStarted()
    {
        UpdateStatus("Game Started!");
    }

    public void HideLobbyUI(bool hideCamera = true)
    {
        // Hide or disable the lobby UI and camera on the client
        if (lobbyCamera != null && hideCamera)
        {
            lobbyCamera.enabled = false;
            lobbyCamera.gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
    }

    private void SafeSetText(TMP_Text t, string msg)
    {
        if (t == null) return;
        t.text = msg;
    }

    private void OnDestroy()
    {
        createLobbyButton?.onClick.RemoveListener(OnCreateLobbyClicked);
        startGameButton?.onClick.RemoveListener(OnStartGameClicked);

        if (memberUpdateCoroutine != null)
            StopCoroutine(memberUpdateCoroutine);

        if (startButtonCoroutine != null)
            StopCoroutine(startButtonCoroutine);
    }
}