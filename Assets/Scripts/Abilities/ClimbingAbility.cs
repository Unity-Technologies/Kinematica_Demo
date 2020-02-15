using Unity.Kinematica;
using Unity.Mathematics;
using UnityEngine;

using SnapshotProvider = Unity.SnapshotDebugger.SnapshotProvider;

[RequireComponent(typeof(AbilityRunner))]
[RequireComponent(typeof(MovementController))]
public partial class ClimbingAbility : SnapshotProvider, Ability
{
    public override void OnEnable()
    {
        base.OnEnable();
    }

    public Ability OnUpdate(float deltaTime)
    {
        return null;
    }

    public bool OnContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, float deltaTime)
    {
        return false;
    }

    public bool OnDrop(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        return false;
    }
}
