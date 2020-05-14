using UnityEngine;

using Unity.Kinematica;
using Unity.Mathematics;
using System;
using UnityEngine.AI;

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

    [Header("Pathfinding")]
    [Range(0.0f, 10.0f)]
    [Tooltip("Quadruped will start moving toward target when the distance between the two become smaller than Desired Distance To Target.")]
    public float desiredDistanceToTarget = 4.0f;

    [Tooltip("If Quadruped is moving toward target, it will stop once the distance between the two become smaller than Target Acceptance Radius.")]
    [Range(0.0f, 10.0f)]
    public float targetAcceptanceRadius = 2.0f;

    [Tooltip("Distance in meters the quadruped needs to go from 0 meter per second to Desired Speed Fast.")]
    [Range(0.0f, 10.0f)]
    public float accelerationDistance = 2.0f;

    [Tooltip("Distance in meters the quadruped needs stop when it's moving at Desired Speed Fast.")]
    [Range(0.0f, 10.0f)]
    public float decelerationDistance = 3.0f;

    public Transform follow;

    [Header("Motion correction")]
    [Tooltip("Quadruped speed in meter per second where motion correction starts to be effective.")]
    [Range(0.0f, 10.0f)]
    public float motionCorrectionStartSpeed = 0.2f;

    [Tooltip("Quadruped speed in meter per second where motion correction becomes fully effective.")]
    [Range(0.0f, 10.0f)]
    public float motionCorrectionEndSpeed = 0.4f;



    Identifier<SelectorTask> locomotion;
    Identifier<NavigationTask> navigation;
    Identifier<Trajectory> desiredTrajectory;

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

            ref NavigationTask navigationTask = ref action.Navigation();

            navigation = navigationTask;

            action.PushConstrained(
                synthesizer.Query.Where(
                    Locomotion.Default).Except(Idle.Default),
                        navigationTask.trajectory);

            desiredTrajectory = navigationTask.trajectory;
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

        ref var nav = ref synthesizer.GetByType<NavigationTask>(navigation).Ref;

        reduce.responsiveness = responsiveness;

        float3 targetPosition = follow.position;
        float3 currentPosition = transform.position;

        float3 deltaPosition = targetPosition - currentPosition;
        
        float distanceToTarget = math.length(deltaPosition);

        if (distanceToTarget > desiredDistanceToTarget)
        {            
            NavMeshPath navMeshPath = new NavMeshPath();
            if (NavMesh.CalculatePath(currentPosition, targetPosition, NavMesh.AllAreas, navMeshPath))
            {
                var navParams = new NavigationParams()
                {
                    desiredSpeed = desiredSpeedFast,
                    maxSpeedAtRightAngle = 0.0f,
                    maximumAcceleration = NavigationParams.ComputeAccelerationToReachSpeed(desiredSpeedFast, accelerationDistance),
                    maximumDeceleration = NavigationParams.ComputeAccelerationToReachSpeed(desiredSpeedFast, decelerationDistance),
                    intermediateControlPointRadius = 0.5f,
                    finalControlPointRadius = targetAcceptanceRadius,
                    pathCurvature = 5.0f
                };

                float3[] points = Array.ConvertAll(navMeshPath.corners, pos => new float3(pos));
                nav.FollowPath(points, navParams);
            }
        }
        
        if (!nav.IsPathValid || nav.GoalReached)
        {
            idle.value = true;
        }
        else
        {
            idle.value = false;
        }
    }

    public virtual void OnAnimatorMove()
    {
#if UNITY_EDITOR
        if (Unity.SnapshotDebugger.Debugger.instance.rewind)
        {
            return;
        }
#endif

        var kinematica = GetComponent<Kinematica>();

        if (kinematica.Synthesizer.IsValid)
        {
            ref MotionSynthesizer synthesizer = ref kinematica.Synthesizer.Ref;


            float correctionFactor = 1.0f;
            ref var idle = ref synthesizer.GetByType<ConditionTask>(locomotion).Ref;
            if (idle.value)
            {
                correctionFactor = 0.0f;
            }

            AffineTransform rootMotion = synthesizer.SteerRootMotion(
                desiredTrajectory, 
                0.0f, // no translation correction
                correctionFactor, // rotation correction
                motionCorrectionStartSpeed, // character speed where correction starts
                motionCorrectionEndSpeed // chracter speed where correction is at maximum
                );

            transform.position = transform.TransformPoint(rootMotion.t);
            transform.rotation *= rootMotion.q;

            synthesizer.SetWorldTransform(AffineTransform.Create(transform.position, transform.rotation), true);
        }
    }
}