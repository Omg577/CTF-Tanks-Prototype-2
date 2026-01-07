using System;
using Unity.Netcode;

public struct TankPickEntryNew : INetworkSerializable, IEquatable<TankPickEntryNew>
{
    public ulong ClientId;
    public TeamIdNew Team;
    public int TankId;     // -1 = none selected
    public bool Locked;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Team);
        serializer.SerializeValue(ref TankId);
        serializer.SerializeValue(ref Locked);
    }

    public bool Equals(TankPickEntryNew other)
        => ClientId == other.ClientId && Team == other.Team && TankId == other.TankId && Locked == other.Locked;
}

public enum TankDraftFailReasonNew : byte
{
    None = 0,
    NotInTankSelect = 1,
    InvalidTankId = 2,
    TankTakenByTeam = 3,
    AlreadyLocked = 4
}
