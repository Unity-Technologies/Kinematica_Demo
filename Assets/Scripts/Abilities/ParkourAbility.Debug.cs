using Unity.SnapshotDebugger;
using UnityEngine;

public partial class ParkourAbility : SnapshotProvider, Ability
{
    public struct FrameCapture
    {
        public bool jumpButton;

        public void WriteToStream(Buffer buffer)
        {
            buffer.Write(jumpButton);
        }

        public void ReadFromStream(Buffer buffer)
        {
            jumpButton = buffer.ReadBoolean();
        }
    }

    public override void OnEarlyUpdate(bool rewind)
    {
        capture.jumpButton = Input.GetButton("A Button");
    }

    public override void WriteToStream(Buffer buffer)
    {
        capture.WriteToStream(buffer);

        buffer.Write(root);
    }

    public override void ReadFromStream(Buffer buffer)
    {
        capture.ReadFromStream(buffer);

        root.index = buffer.Read16();
    }
}
