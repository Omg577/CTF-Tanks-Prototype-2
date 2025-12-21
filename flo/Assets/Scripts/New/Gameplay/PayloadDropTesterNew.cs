using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PayloadDropTesterNew : NetworkBehaviour
{
    [Tooltip("Optional. If left null, we will auto-find the enemy payload at runtime.")]
    [SerializeField] private PayloadNew enemyPayload;

    private TeamComponentNew _teamComp;
    private NetworkObject _myNO;

    public override void OnNetworkSpawn()
    {
        _teamComp = GetComponent<TeamComponentNew>();
        _myNO = GetComponent<NetworkObject>();

        // Try immediately, then we’ll keep trying until found.
        TryAutoAssignEnemyPayload();
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Keep trying to auto-assign until we find it (payloads might spawn after player)
        if (enemyPayload == null)
            TryAutoAssignEnemyPayload();

        var kb = Keyboard.current;
        if (kb != null && kb.qKey.wasPressedThisFrame)
        {
            DropServerRpc();
        }
    }

    private void TryAutoAssignEnemyPayload()
    {
        if (_teamComp == null) return;
        if (_teamComp.Team == TeamIdNew.None) return;

        // Find all payloads (scene-spawned with Spawn On Start, or server-spawned)
#if UNITY_6000_0_OR_NEWER
        var payloads = Object.FindObjectsByType<PayloadNew>(FindObjectsSortMode.None);
#else
        var payloads = Object.FindObjectsOfType<PayloadNew>();
#endif

        foreach (var p in payloads)
        {
            if (p == null) continue;
            if (p.OwnerTeam != _teamComp.Team)
            {
                enemyPayload = p;
                // Debug.Log($"[PayloadDropTesterNew] Auto-assigned enemy payload: {p.name}");
                break;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DropServerRpc()
    {
        if (enemyPayload == null) return;
        if (_myNO == null) _myNO = GetComponent<NetworkObject>();
        if (_myNO == null) return;

        // Only drop if THIS player is the carrier
        if (enemyPayload.IsCarried && enemyPayload.CarrierNetObjectId == _myNO.NetworkObjectId)
        {
            enemyPayload.ServerDrop();
        }
    }
}
