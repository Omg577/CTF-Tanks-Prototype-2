using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class DamageSelfTesterNew : NetworkBehaviour
{
    [SerializeField] private int damageAmount = 25;

    private void Update()
    {
        if (!IsOwner) return;

        var kb = Keyboard.current;
        if (kb != null && kb.kKey.wasPressedThisFrame)
            DamageMeServerRpc(damageAmount);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void DamageMeServerRpc(int amount)
    {
        var health = GetComponent<VehicleHealthNew>();
        if (health != null)
            health.ServerApplyDamage(amount, OwnerClientId);
    }
}
