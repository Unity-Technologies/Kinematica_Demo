using System;
using Unity.Kinematica.Editor;

[Marker("Loop", "Yellow")]
[Serializable]
public struct LoopMarker : Payload<Loop>
{
    public Loop Build(PayloadBuilder builder)
    {
        return Loop.Default;
    }
}
