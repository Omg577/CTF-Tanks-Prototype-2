using Unity.Netcode;
using UnityEngine;

public struct VehicleInputNew : INetworkSerializable
{
    public int Tick;
    public float Throttle; // -1..1
    public float Turn;     // -1..1

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Throttle);
        serializer.SerializeValue(ref Turn);
    }
}

public struct VehicleSimStateNew
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public float YawDegPerSec;
}

public struct VehicleSnapshotNew : INetworkSerializable
{
    public int Tick;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public float YawDegPerSec;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
        serializer.SerializeValue(ref Velocity);
        serializer.SerializeValue(ref YawDegPerSec);
    }

    public VehicleSimStateNew ToSimState() => new VehicleSimStateNew
    {
        Position = Position,
        Rotation = Rotation,
        Velocity = Velocity,
        YawDegPerSec = YawDegPerSec
    };

    public static VehicleSnapshotNew FromSimState(int tick, VehicleSimStateNew s) => new VehicleSnapshotNew
    {
        Tick = tick,
        Position = s.Position,
        Rotation = s.Rotation,
        Velocity = s.Velocity,
        YawDegPerSec = s.YawDegPerSec
    };
}
