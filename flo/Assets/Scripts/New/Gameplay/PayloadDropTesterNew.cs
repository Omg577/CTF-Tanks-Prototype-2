using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PayloadDropTesterNew : NetworkBehaviour
{
    private NetworkObject _myNO;

    public override void OnNetworkSpawn()
    {
        _myNO = GetComponent<NetworkObject>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        var kb = Keyboard.current;
        if (kb != null && kb.qKey.wasPressedThisFrame)
        {
            DropServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DropServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        if (_myNO == null) _myNO = GetComponent<NetworkObject>();
        if (_myNO == null) return;

        // Security: ensure the RPC sender actually owns this vehicle
        ulong sender = rpcParams.Receive.SenderClientId;
        if (_myNO.OwnerClientId != sender) return;

        // Find the payload currently carried by THIS vehicle and drop it
#if UNITY_6000_0_OR_NEWER
        var payloads = Object.FindObjectsByType<PayloadNew>(FindObjectsSortMode.None);
#else
        var payloads = Object.FindObjectsOfType<PayloadNew>();
#endif

        for (int i = 0; i < payloads.Length; i++)
        {
            var p = payloads[i];
            if (p == null) continue;

            if (p.IsCarried && p.CarrierNetObjectId == _myNO.NetworkObjectId)
            {
                p.ServerDrop();
                return;
            }
        }
    }
}
