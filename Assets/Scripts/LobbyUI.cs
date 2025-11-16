using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using System.Collections;
using Steamworks;

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
    private bool isInitialized = false;

    private void Start()
    {
        StartCoroutine(InitializeUI());
    }

    private IEnumerator InitializeUI()
    {
        yield return new WaitForEndOfFrame();
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
            UpdateStatus("Error: NetworkManager not found!");
            yield break;
        }

        if (createLobbyButton != null)
        {
            createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);
            createLobbyButton.interactable = true;
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(OnStartGameClicked);
            startGameButton.interactable = false;
        }

        SafeSetText(instructionsText, "To join a friend's lobby:\n1. Press Shift+Tab (Steam Overlay)\n2. Right-click friend → Join Game");

        UpdateStatus("Ready - Create a lobby or join via Steam overlay");

        isInitialized = true;
        memberUpdateCoroutine = StartCoroutine(UpdateLobbyMembersRoutine());
    }

    private void OnCreateLobbyClicked()
    {
        if (!SteamInitializer.Initialized)
        {
            UpdateStatus("Steam not initialized!");
            return;
        }

        UpdateStatus("Creating lobby...");
        if (createLobbyButton != null)
            createLobbyButton.interactable = false;

        networkManager = networkManager ?? FindFirstObjectByType<SteamNetworkManager>();
        networkManager?.HostLobby();

        if (startGameButton != null)
            startGameButton.interactable = true;
    }

    private void OnStartGameClicked()
    {
        UpdateStatus("Starting game...");

        if (networkManager != null)
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
        if (lobbyId == null || !lobbyId.IsValid())
        {
            SafeSetText(lobbyMembersText, "Not in lobby");
            return;
        }

        int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Lobby Members ({memberCount}):");

        for (int i = 0; i < memberCount; i++)
        {
            CSteamID memberID = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
            string name = "(unknown)";
            try
            {
                name = SteamFriends.GetFriendPersonaName(memberID);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[LobbyUI] Failed to get member name: {e.Message}");
            }
            sb.AppendLine($"- {name}");
        }

        SafeSetText(lobbyMembersText, sb.ToString());
    }

    public void UpdateStatus(string message)
    {
        SafeSetText(statusText, message);
        Debug.Log($"[LobbyUI] {message}");
    }

    private void SafeSetText(TMP_Text textComponent, string message)
    {
        if (textComponent == null)
            return;

        try
        {
            textComponent.text = message;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyUI] Failed to set text: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        if (createLobbyButton != null)
            createLobbyButton.onClick.RemoveListener(OnCreateLobbyClicked);

        if (startGameButton != null)
            startGameButton.onClick.RemoveListener(OnStartGameClicked);

        if (memberUpdateCoroutine != null)
            StopCoroutine(memberUpdateCoroutine);
    }
}