using UnityEngine;

using Unity.Kinematica;
using Unity.Mathematics;

[RequireComponent(typeof(Kinematica))]
public class Quadruped : MonoBehaviour
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
    
    [Tooltip("Relative weighting for pose and trajectory matching.")]
    [Range(0.0f, 1.0f)]
    public float responsiveness = 0.5f;

    public Transform follow;

    Identifier<SelectorTask> locomotion;

    float3 movementDirection = Missing.forward;

    bool wantsIdle;

    void OnEnable()
    {
        var kinematica = GetComponent<Kinematica>();

        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        synthesizer.Push(
            synthesizer.Query.Where(
                Locomotion.Default).And(Idle.Default));

        var selector = synthesizer.Selector();

        {
            var sequence = selector.Condition().Sequence();

            sequence.Action().PushConstrained(
                synthesizer.Query.Where(
                    Locomotion.Default).And(Idle.Default), 0.1f);

            sequence.Action().Timer();
        }

        {
            var action = selector.Action();

            action.PushConstrained(
                synthesizer.Query.Where(
                    Locomotion.Default).Except(Idle.Default),
                        action.TrajectoryPrediction().trajectory);
        }

        locomotion = selector;
    }

    void Update()
    {
        var kinematica = GetComponent<Kinematica>();

        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        synthesizer.Tick(locomotion);

        ref var prediction = ref synthesizer.GetByType<TrajectoryPredictionTask>(locomotion).Ref;
        ref var idle = ref synthesizer.GetByType<ConditionTask>(locomotion).Ref;

        ref var reduce = ref synthesizer.GetByType<ReduceTask>(locomotion).Ref;

        reduce.responsiveness = responsiveness;

        float3 targetPosition = follow.position;
        float3 currentPosition = transform.position;

        float3 deltaPosition = targetPosition - currentPosition;
        
        float distanceToTarget = math.length(deltaPosition);

        float3 relativeDesiredVelocity =
            math.normalizesafe(deltaPosition, Missing.forward);

        float desiredLinearSpeed = desiredSpeedSlow;

        if (distanceToTarget <= 1.5f)
        {
            wantsIdle = true;
        }

        if (wantsIdle && distanceToTarget > 2.0f)
        {
            wantsIdle = false;
        }

        if (wantsIdle)
        {
            relativeDesiredVelocity = float3.zero;
        }
        else if (math.dot(relativeDesiredVelocity, transform.forward) >= 0.85f)
        {
            desiredLinearSpeed = desiredSpeedFast;
        }

        prediction.velocityFactor = velocityPercentage;
        prediction.rotationFactor = forwardPercentage;

        idle.value = math.length(relativeDesiredVelocity) <= 0.1f;

        if (idle)
        {
            prediction.linearSpeed = 0.0f;
        }
        else
        {
            movementDirection = math.normalizesafe(
                relativeDesiredVelocity, movementDirection);

            prediction.linearSpeed =
                math.length(relativeDesiredVelocity) *
                    desiredLinearSpeed;

            prediction.movementDirection = movementDirection;
            prediction.forwardDirection = movementDirection;
        }
    }
}