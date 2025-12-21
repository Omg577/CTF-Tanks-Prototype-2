using Unity.Netcode;
using UnityEngine;

public class TeamComponentNew : NetworkBehaviour
{
    // Store as byte for netcode friendliness
    private readonly NetworkVariable<byte> teamId = new(
        (byte)TeamIdNew.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public TeamIdNew Team => (TeamIdNew)teamId.Value;

    // Server-only setter
    public void ServerSetTeam(TeamIdNew team)
    {
        if (!IsServer) return;
        teamId.Value = (byte)team;
    }
}
