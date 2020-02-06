using Unity.SnapshotDebugger;
using UnityEngine;

public partial class LocomotionAbility : SnapshotProvider, Ability
{
    public override void OnEarlyUpdate(bool rewind)
    {
        if (!rewind)
        {
            cameraForward = Camera.main.transform.forward;

            horizontal = Input.GetAxis("Left Analog Horizontal");
            vertical = Input.GetAxis("Left Analog Vertical");

            run = Input.GetButton("A Button");
        }
    }

    public override void WriteToStream(Buffer buffer)
    {
        buffer.Write(horizontal);
        buffer.Write(vertical);
        buffer.Write(run);

        buffer.Write(cameraForward);
        buffer.Write(movementDirection);
        buffer.Write(forwardDirection);
        buffer.Write(linearSpeed);
        buffer.Write(previousFrameCount);
    }

    public override void ReadFromStream(Buffer buffer)
    {
        horizontal = buffer.ReadSingle();
        vertical = buffer.ReadSingle();
        run = buffer.ReadBoolean();

        cameraForward = buffer.ReadVector3();
        movementDirection = buffer.ReadVector3();
        forwardDirection = buffer.ReadVector3();
        linearSpeed = buffer.ReadSingle();
        previousFrameCount = buffer.Read32();
    }
}
