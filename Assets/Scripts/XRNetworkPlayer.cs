using UnityEngine;
using Mirror;

public class XRNetworkPlayer : NetworkBehaviour
{
    [Header("VR Components")]
    public Transform body;      // Capsule representing the body
    public Transform head;      // Cube representing the head
    public Transform leftHand;  
    public Transform rightHand; 

    [Header("Networked Dummies (for remote players)")]
    public Transform netBody;
    public Transform netHead;
    public Transform netLeftHand;
    public Transform netRightHand;

    [Header("VR Settings")]
    public float updateRate = 0.05f; // How often to send updates to server
    private float nextUpdateTime = 0f;

    void Start()
    {
        if (isLocalPlayer)
        {
            // Hide networked dummy objects for local player
            if (netBody) netBody.gameObject.SetActive(false);
            if (netHead) netHead.gameObject.SetActive(false);
            if (netLeftHand) netLeftHand.gameObject.SetActive(false);
            if (netRightHand) netRightHand.gameObject.SetActive(false);

            // Enable VR camera (assumes it's child of head)
            Camera xrCam = GetComponentInChildren<Camera>();
            if (xrCam)
            {
                xrCam.enabled = true;
                xrCam.gameObject.SetActive(true);
            }
        }
        else
        {
            // Hide local transforms for remote players
            if (body) body.gameObject.SetActive(false);
            if (head) head.gameObject.SetActive(false);
            if (leftHand) leftHand.gameObject.SetActive(false);
            if (rightHand) rightHand.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (!isLocalPlayer) return;

        if (Time.time >= nextUpdateTime)
        {
            nextUpdateTime = Time.time + updateRate;

            // Send VR rig position and rotations to server
            CmdSendTransforms(
                body.position, body.rotation,
                head.position, head.rotation,
                leftHand.position, leftHand.rotation,
                rightHand.position, rightHand.rotation
            );
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
        if (netBody) netBody.SetPositionAndRotation(bodyPos, bodyRot);
        if (netHead) netHead.SetPositionAndRotation(headPos, headRot);
        if (netLeftHand) netLeftHand.SetPositionAndRotation(leftPos, leftRot);
        if (netRightHand) netRightHand.SetPositionAndRotation(rightPos, rightRot);
    } 
}
