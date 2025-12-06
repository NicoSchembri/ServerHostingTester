using UnityEngine;
using Mirror;

public class XRNetworkPlayer : NetworkBehaviour
{
    [Header("VR Components (Local Rig)")]
    public Transform body;
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Networked Dummies (Remote Representation)")]
    public Transform netBody;
    public Transform netHead;
    public Transform netLeftHand;
    public Transform netRightHand;

    [Header("VR Settings")]
    public float updateRate = 0.05f;
    private float nextUpdateTime = 0f;

    private Vector3 tBodyPos, tHeadPos, tLeftPos, tRightPos;
    private Quaternion tBodyRot, tHeadRot, tLeftRot, tRightRot;

    [Tooltip("How fast remote avatars reach the latest received transforms")]
    public float remoteLerpSpeed = 12f;

    public Camera playerCamera;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Debug.Log("[XRNetworkPlayer] OnStartLocalPlayer");

        EnableGameplayCamera();

        if (netBody) netBody.gameObject.SetActive(false);
        if (netHead) netHead.gameObject.SetActive(false);
        if (netLeftHand) netLeftHand.gameObject.SetActive(false);
        if (netRightHand) netRightHand.gameObject.SetActive(false);

        CmdNotifyServerPlayerSpawned();
    }

    void Start()
    {
        if (!isLocalPlayer)
        {
            if (body) body.gameObject.SetActive(false);
            if (head) head.gameObject.SetActive(false);
            if (leftHand) leftHand.gameObject.SetActive(false);
            if (rightHand) rightHand.gameObject.SetActive(false);

            if (netBody) { tBodyPos = netBody.position; tBodyRot = netBody.rotation; }
            if (netHead) { tHeadPos = netHead.position; tHeadRot = netHead.rotation; }
            if (netLeftHand) { tLeftPos = netLeftHand.position; tLeftRot = netLeftHand.rotation; }
            if (netRightHand) { tRightPos = netRightHand.position; tRightRot = netRightHand.rotation; }
        }
        else
        {
            EnableGameplayCamera();
        }
    }

    void Update()
    {
        if (!isLocalPlayer)
        {
            float dt = Time.deltaTime * remoteLerpSpeed;

            if (netBody) netBody.SetPositionAndRotation(
                Vector3.Lerp(netBody.position, tBodyPos, dt),
                Quaternion.Slerp(netBody.rotation, tBodyRot, dt)
            );

            if (netHead) netHead.SetPositionAndRotation(
                Vector3.Lerp(netHead.position, tHeadPos, dt),
                Quaternion.Slerp(netHead.rotation, tHeadRot, dt)
            );

            if (netLeftHand) netLeftHand.SetPositionAndRotation(
                Vector3.Lerp(netLeftHand.position, tLeftPos, dt),
                Quaternion.Slerp(netLeftHand.rotation, tLeftRot, dt)
            );

            if (netRightHand) netRightHand.SetPositionAndRotation(
                Vector3.Lerp(netRightHand.position, tRightPos, dt),
                Quaternion.Slerp(netRightHand.rotation, tRightRot, dt)
            );

            return;
        }

        if (Time.time >= nextUpdateTime)
        {
            nextUpdateTime = Time.time + Mathf.Max(0.01f, updateRate);

            if (body == null || head == null || leftHand == null || rightHand == null)
                return;

            CmdSendTransforms(
                body.position, body.rotation,
                head.position, head.rotation,
                leftHand.position, leftHand.rotation,
                rightHand.position, rightHand.rotation
            );
        }
    }

    public void EnableGameplayCamera()
    {
        if (!isLocalPlayer)
            return;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(true);
            playerCamera.enabled = true;
            Debug.Log("[XRNetworkPlayer] Gameplay camera enabled for local player");
        }
        else
        {
            Debug.LogWarning("[XRNetworkPlayer] No playerCamera found to enable");
        }
    }

    [Command]
    void CmdSendTransforms(
        Vector3 bodyPos, Quaternion bodyRot,
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot
    )
    {
        RpcApplyTransforms(bodyPos, bodyRot, headPos, headRot, leftPos, leftRot, rightPos, rightRot);
    }

    [ClientRpc(includeOwner = false)]
    void RpcApplyTransforms(
        Vector3 bodyPos, Quaternion bodyRot,
        Vector3 headPos, Quaternion headRot,
        Vector3 leftPos, Quaternion leftRot,
        Vector3 rightPos, Quaternion rightRot
    )
    {
        if (netBody) { tBodyPos = bodyPos; tBodyRot = bodyRot; }
        if (netHead) { tHeadPos = headPos; tHeadRot = headRot; }
        if (netLeftHand) { tLeftPos = leftPos; tLeftRot = leftRot; }
        if (netRightHand) { tRightPos = rightPos; tRightRot = rightRot; }
    }

    [Command]
    void CmdNotifyServerPlayerSpawned()
    {
        var manager = FindFirstObjectByType<SteamNetworkManager>();
        if (manager != null)
        {
            // No longer needed to call NotifyPlayerSpawned
            Debug.Log("[XRNetworkPlayer] CmdNotifyServerPlayerSpawned invoked");
        }
        else
        {
            Debug.LogWarning("[XRNetworkPlayer] CmdNotifyServerPlayerSpawned: SteamNetworkManager not found on server");
        }
    }

    [TargetRpc]
    public void TargetActivateCamera(NetworkConnection target)
    {
        // Hide lobby UI and camera
        var lobby = FindFirstObjectByType<LobbyUI>();
        if (lobby != null)
        {
            if (lobby.lobbyCamera != null)
            {
                lobby.lobbyCamera.enabled = false;
                lobby.lobbyCamera.gameObject.SetActive(false);
            }
            lobby.gameObject.SetActive(false);
        }

        // Enable local VR camera
        EnableGameplayCamera();
    }
}
