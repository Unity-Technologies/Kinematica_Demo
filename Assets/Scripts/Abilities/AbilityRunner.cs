using Unity.Kinematica;
using Unity.Mathematics;
using Unity.SnapshotDebugger;

using UnityEngine;
using UnityEngine.Assertions;

[RequireComponent(typeof(MovementController))]
public class AbilityRunner : Kinematica
{
    public virtual new void Update()
    {
        // Now iterate all abilities and update each one in turn.
        foreach (Ability ability in GetComponents(typeof(Ability)))
        {
            // An ability can either return "null" or a reference to an ability.
            // A "null" result signals that this ability doesn't require control.
            // Otherwise the returned ability (which might be different from the
            // one that we call "OnUpdate" on) will be the one that gains control.
            Ability result = ability.OnUpdate(_deltaTime);

            if (result != null)
            {
                break;
            }
        }

        base.Update();
    }

    public override void OnAnimatorMove()
    {
        ref var synthesizer = ref Synthesizer.Ref;

        var controller = GetComponent<MovementController>();

        Assert.IsTrue(controller != null);

        float3 controllerPosition = controller.Position;

        float3 desiredLinearDisplacement =
            synthesizer.WorldRootTransform.t -
                controllerPosition;

        controller.Move(desiredLinearDisplacement);
        controller.Tick(
            Debugger.instance.deltaTime);

        float3 actualLinearDisplacement =
            controller.Position - controllerPosition;

        float3 deltaLinearDisplacement =
            desiredLinearDisplacement - actualLinearDisplacement;

        MemoryArray<AffineTransform> trajectory = synthesizer.TrajectoryArray;
        int halfTrajectoryLength = trajectory.Length / 2;
        for (int i = 0; i < halfTrajectoryLength; ++i)
        {
            trajectory[
                halfTrajectoryLength + i].t -=
                    deltaLinearDisplacement;
        }

        var worldRootTransform = synthesizer.WorldRootTransform;

        worldRootTransform.t = controller.Position;

        transform.position = worldRootTransform.t;
        transform.rotation = worldRootTransform.q;

        synthesizer.WorldRootTransform = worldRootTransform;
    }
}
