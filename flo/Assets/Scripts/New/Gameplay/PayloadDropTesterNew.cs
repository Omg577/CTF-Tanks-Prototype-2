using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PayloadDropTesterNew : NetworkBehaviour
{
    [SerializeField] private PayloadNew enemyPayload; // assign in inspector

    private void Update()
    {
        if (!IsOwner) return;
        if (enemyPayload == null) return;

        var kb = Keyboard.current;
        if (kb != null && kb.qKey.wasPressedThisFrame)
        {
            DropServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DropServerRpc()
    {
        if (enemyPayload != null)
            enemyPayload.ServerDrop();
    }
}
