using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class CaptureZoneNew : NetworkBehaviour
{
    [Header("Zone")]
    [SerializeField] private TeamIdNew zoneTeam = TeamIdNew.TeamA;

    [Header("Payload references")]
    [Tooltip("The enemy payload that must be brought into this zone to score.")]
    [SerializeField] private PayloadNew enemyPayload;

    [Tooltip("Optional: if enabled, scoring requires your own payload to be at home.")]
    [SerializeField] private bool requireOwnPayloadHome = true;

    [Tooltip("If requireOwnPayloadHome is enabled, assign your OWN payload here.")]
    [SerializeField] private PayloadNew ownPayload;

    private void Awake()
    {
        // Ensure trigger
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"{name}: CaptureZone collider should be trigger. Setting isTrigger=true.");
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (enemyPayload == null) return;

        var teamComp = other.GetComponentInParent<TeamComponentNew>();
        if (teamComp == null) return;

        var playerNO = other.GetComponentInParent<NetworkObject>();
        if (playerNO == null) return;

        // Must be the zone's team
        if (teamComp.Team != zoneTeam) return;

        // Optional rule: own payload must be home
        if (requireOwnPayloadHome)
        {
            if (ownPayload == null) return;
            if (ownPayload.IsCarried || ownPayload.IsDropped) return;
        }

        // Enemy payload must be carried by THIS player
        if (!enemyPayload.IsCarried) return;
        if (enemyPayload.CarrierNetObjectId != playerNO.NetworkObjectId) return;

        // Score!
        if (ScoreManagerNew.Instance != null)
            ScoreManagerNew.Instance.ServerAddPoint(zoneTeam);

        // Return enemy payload home after scoring
        enemyPayload.ServerReturnHome();
    }
}
