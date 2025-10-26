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
    public TMP_Text statusText;
    public TMP_Text instructionsText;
    public TMP_Text lobbyMembersText;

    private SteamNetworkManager networkManager;
    private Coroutine memberUpdateCoroutine;

    private void Start()
    {
        StartCoroutine(InitializeUI());
    }

    private IEnumerator InitializeUI()
    {
        // wait until Steam initialized (safe to call Steam API on main thread)
        while (!SteamInitializer.Initialized)
        {
            UpdateStatus("Waiting for Steam...");
            yield return null;
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

        if (instructionsText != null)
        {
            instructionsText.text = "To join a friend's lobby:\n1. Press Shift+Tab (Steam Overlay)\n2. Right-click friend → Join Game";
        }

        UpdateStatus("Ready - Create a lobby or join via Steam overlay");

        // start periodic member update
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
    }

    private IEnumerator UpdateLobbyMembersRoutine()
    {
        while (true)
        {
            UpdateLobbyMembers();
            yield return new WaitForSeconds(2f);
        }
    }

    private void UpdateLobbyMembers()
    {
        if (networkManager == null || lobbyMembersText == null)
            return;

        CSteamID lobbyId = networkManager.CurrentLobbyID;
        // check validity using IsValid (safer than comparing to Nil)
        if (lobbyId == null || !lobbyId.IsValid())
            return;

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
            catch
            {
                // fallback, keep unknown
            }
            sb.AppendLine($"- {name}");
        }

        lobbyMembersText.text = sb.ToString();
    }

    public void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[LobbyUI] {message}");
    }

    private void OnDestroy()
    {
        if (createLobbyButton != null)
            createLobbyButton.onClick.RemoveListener(OnCreateLobbyClicked);

        if (memberUpdateCoroutine != null)
            StopCoroutine(memberUpdateCoroutine);
    }
}
