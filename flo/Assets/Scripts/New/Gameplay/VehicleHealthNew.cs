using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class VehicleHealthNew : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] private int maxHealth = 100;

    private readonly NetworkVariable<int> currentHealth = new(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isDead = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ServerTime until which damage is ignored (spawn protection)
    private readonly NetworkVariable<double> invulnerableUntilServerTime = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int MaxHealth => maxHealth;
    public int CurrentHealth => currentHealth.Value;
    public bool IsDead => isDead.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;
            invulnerableUntilServerTime.Value = 0;
        }
    }

    // ---- Server API ----
    public void ServerSetInvulnerableSeconds(float seconds)
    {
        if (!IsServer) return;
        if (NetworkManager.Singleton == null) return;

        invulnerableUntilServerTime.Value = NetworkManager.Singleton.ServerTime.Time + seconds;
    }

    public void ServerResetFullHealth()
    {
        if (!IsServer) return;
        currentHealth.Value = maxHealth;
        isDead.Value = false;
        invulnerableUntilServerTime.Value = 0;
    }

    public void ServerApplyDamage(int amount, ulong instigatorClientId = ulong.MaxValue)
    {
        if (!IsServer) return;
        if (!IsSpawned) return;
        if (amount <= 0) return;
        if (isDead.Value) return;

        // Only allow death/damage while match is in game
        var gsm = GameStateManagerNew.Instance;
        if (gsm != null && gsm.State != MatchStateNew.InGame) return;

        double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : 0;
        if (now < invulnerableUntilServerTime.Value) return;

        int newHp = Mathf.Max(0, currentHealth.Value - amount);
        currentHealth.Value = newHp;

        if (newHp == 0)
        {
            isDead.Value = true;

            // Inform central respawn manager
            if (RespawnManagerNew.Instance != null)
            {
                RespawnManagerNew.Instance.ServerHandleDeath(GetComponent<NetworkObject>().NetworkObjectId, instigatorClientId);
            }
        }
    }

    // RespawnManager calls this on server
    public void ServerMarkDead(bool dead)
    {
        if (!IsServer) return;
        isDead.Value = dead;
    }
}
