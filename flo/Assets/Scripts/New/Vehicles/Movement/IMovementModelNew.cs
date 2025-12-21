public interface IMovementModelNew
{
    VehicleSimStateNew Step(VehicleSimStateNew state, VehicleInputNew input, float dt);
}
