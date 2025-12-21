using UnityEngine;

public abstract class MovementModelConfigNew : ScriptableObject
{
    public abstract IMovementModelNew CreateRuntimeModel();
}
