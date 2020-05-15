using Unity.SnapshotDebugger;
using UnityEngine;

public partial class ClimbingAbility : SnapshotProvider, Ability
{
    public struct FrameCapture
    {
        public float stickHorizontal;
        public float stickVertical;
        public bool mountButton;
        public bool dismountButton;
        public bool pullUpButton;

        public void WriteToStream(Buffer buffer)
        {
            buffer.Write(stickHorizontal);
            buffer.Write(stickVertical);
            buffer.Write(mountButton);
            buffer.Write(dismountButton);
            buffer.Write(pullUpButton);
        }

        public void ReadFromStream(Buffer buffer)
        {
            stickHorizontal = buffer.ReadSingle();
            stickVertical = buffer.ReadSingle();
            mountButton = buffer.ReadBoolean();
            dismountButton = buffer.ReadBoolean();
            pullUpButton = buffer.ReadBoolean();
        }

        public void Update()
        {
            stickHorizontal = Input.GetAxis("Left Analog Horizontal");
            stickVertical = Input.GetAxis("Left Analog Vertical");

            mountButton = Input.GetButton("B Button") || Input.GetKey("b");
            dismountButton = Input.GetButton("B Button") || Input.GetKey("b");
            pullUpButton = Input.GetButton("A Button") || Input.GetKey("a");
        }
    }

    public override void OnEarlyUpdate(bool rewind)
    {
        capture.Update();
    }

    public override void WriteToStream(Buffer buffer)
    {
        capture.WriteToStream(buffer);

        ledgeGeometry.WriteToStream(buffer);
        ledgeAnchor.WriteToStream(buffer);

        wallGeometry.WriteToStream(buffer);
        wallAnchor.WriteToStream(buffer);

        buffer.Write(transition.uniqueIdentifier);
        buffer.Write(locomotion.uniqueIdentifier);
    }

    public override void ReadFromStream(Buffer buffer)
    {
        capture.ReadFromStream(buffer);

        ledgeGeometry.ReadFromStream(buffer);
        ledgeAnchor.ReadFromStream(buffer);

        wallGeometry.ReadFromStream(buffer);
        wallAnchor.ReadFromStream(buffer);

        transition.uniqueIdentifier = buffer.Read32();
        locomotion.uniqueIdentifier = buffer.Read32();
    }
}
