using Unity.Kinematica;
using Unity.Mathematics;

using UnityEngine;
using UnityEngine.Assertions;
using System.Collections.Generic;

using Unity.SnapshotDebugger;

[RequireComponent(typeof(MovementController))]
public class AbilityRunner : Kinematica
{
    [Header("Prediction settings")]
    [Tooltip("Output camera look-at transform that will be used by Cinemachine.")]
    public Transform cameraLookAt;

    [Tooltip("Hips joint of the character, used for computing camera-look at point in some situations.")]
    public Transform hipsJoint;

    [Tooltip("Damping duration of the camera look-at horizontal position, doesn't affect camera height.")]
    [Range(0.0f, 1.0f)]
    public float cameraHorizontalDampingDuration = 0.1f;

    [Tooltip("Damping duration of the camera look-at height.")]
    [Range(0.0f, 1.0f)]
    public float cameraVerticalDampingDuration = 0.5f;

    Ability currentAbility;

    SmoothValue2 smoothCameraFollowPos = new SmoothValue2(float2.zero);
    SmoothValue smoothCameraFollowHeight = new SmoothValue(0.0f);

    void AddAbilityDebugRecord(Ability ability)
    {
        AbilityRecord record = new AbilityRecord()
        {
            abilityType = ability.GetType()
        };        

        Debugger.frameDebugger.AddFrameRecord<AbilityFrameAggregate>(this, record);
    }

    public virtual new void Update()
    {
        if (currentAbility != null)
        {
            currentAbility = currentAbility.OnUpdate(_deltaTime);
        }

        if (currentAbility == null)
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
                    currentAbility = result;
                    AddAbilityDebugRecord(currentAbility);
                    break;
                }
            }
        }
        else
        {
            AddAbilityDebugRecord(currentAbility);
        }

        base.Update();
    }

    public override void OnAnimatorMove()
    {
        ref var synthesizer = ref Synthesizer.Ref;

        if (currentAbility is AbilityAnimatorMove abilityAnimatorMove)
        {
            abilityAnimatorMove.OnAbilityAnimatorMove();
        }

        var controller = GetComponent<MovementController>();

        Assert.IsTrue(controller != null);

        float3 controllerPosition = controller.Position;

        float3 desiredLinearDisplacement =
            synthesizer.WorldRootTransform.t -
                controllerPosition;

        controller.Move(desiredLinearDisplacement);
        controller.Tick(
            Debugger.instance.deltaTime);

        var worldRootTransform = AffineTransform.Create(controller.Position, synthesizer.WorldRootTransform.q);

        synthesizer.SetWorldTransform(worldRootTransform, true);

        transform.position = worldRootTransform.t;
        transform.rotation = worldRootTransform.q;        
    }

    public override void LateUpdate()
    {
        base.LateUpdate();

        float3 desiredCameraFollow = (currentAbility == null || currentAbility.UseRootAsCameraFollow) ? transform.position : hipsJoint.position - Vector3.up;

        smoothCameraFollowPos.CriticallyDampedSpring(new float2(desiredCameraFollow.x, desiredCameraFollow.z), Time.deltaTime, cameraHorizontalDampingDuration);
        smoothCameraFollowHeight.CriticallyDampedSpring(desiredCameraFollow.y, Time.deltaTime, cameraVerticalDampingDuration);

        cameraLookAt.position = new float3(smoothCameraFollowPos.CurrentValue.x, smoothCameraFollowHeight.CurrentValue + 1.0f, smoothCameraFollowPos.CurrentValue.y);
    }
}
