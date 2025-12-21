using Unity.Netcode;
using UnityEngine;

public class TeamComponentNew : NetworkBehaviour
{
    [SerializeField] private TeamIdNew team = TeamIdNew.None;

    // For MVP: set in inspector per prefab/team-spawn variant
    public TeamIdNew Team => team;
}
