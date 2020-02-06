using Unity.Kinematica;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

using SnapshotProvider = Unity.SnapshotDebugger.SnapshotProvider;

[RequireComponent(typeof(AbilityRunner))]
[RequireComponent(typeof(MovementController))]
public partial class LocomotionAbility : SnapshotProvider, Ability
{
    [Header("Prediction settings")]
    [Tooltip("Desired speed in meters per second for slow movement.")]
    [Range(0.0f, 10.0f)]
    public float desiredSpeedSlow = 3.9f;

    [Tooltip("Desired speed in meters per second for fast movement.")]
    [Range(0.0f, 10.0f)]
    public float desiredSpeedFast = 5.5f;

    [Tooltip("How fast or slow the target velocity is supposed to be reached.")]
    [Range(0.0f, 1.0f)]
    public float velocityPercentage = 1.0f;

    [Tooltip("How fast or slow the desired forward direction is supposed to be reached.")]
    [Range(0.0f, 1.0f)]
    public float forwardPercentage = 1.0f;

    Identifier<SelectorTask> locomotion;

    float3 cameraForward = Missing.forward;
    float3 movementDirection = Missing.forward;
    float3 forwardDirection = Missing.forward;
    float linearSpeed;

    float horizontal;
    float vertical;
    bool run;

    Identifier<Trajectory> trajectory;

    float desiredLinearSpeed => run ? desiredSpeedFast : desiredSpeedSlow;

    int previousFrameCount;
    
    public override void OnEnable()
    {
        base.OnEnable();

        previousFrameCount = -1;
    }

    public Ability OnUpdate(float deltaTime)
    {
        var kinematica = GetComponent<Kinematica>();

        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        if (previousFrameCount != Time.frameCount - 1)
        {
            var selector = synthesizer.Selector();

            {
                var sequence = selector.Condition().Sequence();

                sequence.Action().PushConstrained(
                    synthesizer.Query.Where(
                        Locomotion.Default).And(Idle.Default), 0.01f);

                sequence.Action().Timer();
            }

            {
                var action = selector.Action();

                this.trajectory = action.Trajectory();

                action.PushConstrained(
                    synthesizer.Query.Where(
                        Locomotion.Default).Except(Idle.Default),
                            this.trajectory);
            }

            locomotion = selector;
        }

        previousFrameCount = Time.frameCount;

        synthesizer.Tick(locomotion);

        ref var idle = ref synthesizer.GetByType<ConditionTask>(locomotion).Ref;

        float3 analogInput = Utility.GetAnalogInput(horizontal, vertical);

        idle.value = math.length(analogInput) <= 0.1f;

        if (idle)
        {
            linearSpeed = 0.0f;
        }
        else
        {
            movementDirection =
                Utility.GetDesiredForwardDirection(
                    analogInput, movementDirection, cameraForward);

            linearSpeed =
                math.length(analogInput) *
                    desiredLinearSpeed;

            forwardDirection = movementDirection;
        }

        var desiredVelocity = movementDirection * linearSpeed;

        var desiredRotation =
            Missing.forRotation(Missing.forward, forwardDirection);

        var trajectory =
            synthesizer.GetArray<AffineTransform>(
                this.trajectory);

        synthesizer.trajectory.Array.CopyTo(ref trajectory);

        var prediction = TrajectoryPrediction.Create(
            ref synthesizer, desiredVelocity, desiredRotation,
                trajectory, velocityPercentage, forwardPercentage);

        var controller = GetComponent<MovementController>();

        Assert.IsTrue(controller != null);

        controller.Snapshot();

        var transform = prediction.Transform;

        var worldRootTransform = synthesizer.WorldRootTransform;

        float inverseSampleRate =
            Missing.recip(synthesizer.Binary.SampleRate);

        while (prediction.Push(transform))
        {
            transform = prediction.Advance;

            controller.MoveTo(worldRootTransform.transform(transform.t));
            controller.Tick(inverseSampleRate);

            transform.t =
                worldRootTransform.inverseTransform(
                    controller.Position);

            prediction.Transform = transform;
        }

        controller.Rewind();

        return this;
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
