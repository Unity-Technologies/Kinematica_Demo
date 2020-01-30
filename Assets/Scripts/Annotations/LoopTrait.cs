using Unity.Burst;
using Unity.Kinematica;

[Trait, BurstCompile]
public struct Loop : Trait
{
    public void Execute(ref MotionSynthesizer synthesizer)
    {
        synthesizer.Push(synthesizer.Rewind(synthesizer.Time));
    }

    [BurstCompile]
    public static void ExecuteSelf(ref Loop self, ref MotionSynthesizer synthesizer)
    {
        self.Execute(ref synthesizer);
    }

    public static Loop Default => new Loop();
}
