using UnityEngine;

using Unity.Kinematica;
using Unity.Mathematics;
using System;
using UnityEngine.AI;
using Unity.SnapshotDebugger;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;

[BurstCompile(CompileSynchronously = true)]
public struct QuadrupedJob : IJob
{
    public MemoryRef<MotionSynthesizer> synthesizer;

    public PoseSet idlePoses;

    public PoseSet locomotionPoses;

    public Trajectory trajectory;

    public MemoryRef<NavigationPath> navigationPath;

    public float responsiveness;

    ref MotionSynthesizer Synthesizer => ref synthesizer.Ref;

    ref NavigationPath NavPath => ref navigationPath.Ref;

    public void Execute()
    {
        bool goalReached = true;

        if (navigationPath.IsValid)
        {
            if (!NavPath.IsBuilt)
            {
                NavPath.Build();
            }

            goalReached = NavPath.GoalReached || !NavPath.UpdateAgentTransform(Synthesizer.WorldRootTransform);
        }

        if (goalReached)
        {
            Synthesizer.ClearTrajectory(trajectory);

            if (Synthesizer.MatchPose(idlePoses, Synthesizer.Time, MatchOptions.DontMatchIfCandidateIsPlaying | MatchOptions.LoopSegment, 0.1f))
            {
                return;
            }
        }
        else
        {
            NavPath.GenerateTrajectory(ref Synthesizer, ref trajectory);
        }

        Synthesizer.MatchPoseAndTrajectory(locomotionPoses, Synthesizer.Time, trajectory, MatchOptions.None, responsiveness);
    }
}

[RequireComponent(typeof(Kinematica))]
public class Quadruped : SnapshotProvider
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

    Kinematica kinematica;

    PoseSet locomotionPoses;
    PoseSet idlePoses;
    Trajectory trajectory;

    [Snapshot]
    NavigationPath navigationPath;

    bool wantsIdle;

    public override void OnEnable()
    {
        base.OnEnable();

        kinematica = GetComponent<Kinematica>();
        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        idlePoses = synthesizer.Query.Where("Idle", Locomotion.Default).And(Idle.Default);
        locomotionPoses = synthesizer.Query.Where("Locomotion", Locomotion.Default).Except(Idle.Default);
        trajectory = synthesizer.CreateTrajectory(Allocator.Persistent);

        navigationPath = NavigationPath.CreateInvalid();

        synthesizer.PlayFirstSequence(idlePoses);
    }

    public override void OnDisable()
    {
        base.OnDisable();

        idlePoses.Dispose();
        locomotionPoses.Dispose();
        trajectory.Dispose();
        navigationPath.Dispose();
    }

    public override void OnEarlyUpdate(bool rewind)
    {
        base.OnEarlyUpdate(rewind);

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


                navigationPath.Dispose();
                navigationPath = NavigationPath.Create(points, AffineTransform.CreateGlobal(transform), navParams, Allocator.Persistent);
            }
        }
    }

    void Update()
    {
        QuadrupedJob job = new QuadrupedJob()
        {
            synthesizer = kinematica.Synthesizer,
            idlePoses = idlePoses,
            locomotionPoses = locomotionPoses,
            trajectory = trajectory,
            navigationPath = navigationPath.IsValid ? new MemoryRef<NavigationPath>(ref navigationPath) : MemoryRef<NavigationPath>.Null,
        };
        kinematica.AddJobDependency(job.Schedule());
    }

    public virtual void OnAnimatorMove()
    {
#if UNITY_EDITOR
        if (Unity.SnapshotDebugger.Debugger.instance.rewind)
        {
            return;
        }
#endif

        if (kinematica.Synthesizer.IsValid)
        {
            ref MotionSynthesizer synthesizer = ref kinematica.Synthesizer.Ref;

            bool idle = !navigationPath.IsValid || navigationPath.GoalReached;
            float correctionFactor = idle ? 0.0f : 1.0f;

            AffineTransform rootMotion = synthesizer.SteerRootMotion(
                trajectory, 
                0.0f, // no translation correction
                correctionFactor, // rotation correction
                motionCorrectionStartSpeed, // character speed where correction starts
                motionCorrectionEndSpeed // chracter speed where correction is at maximum
                );

            transform.position = transform.TransformPoint(rootMotion.t);
            transform.rotation *= rootMotion.q;

            synthesizer.SetWorldTransform(AffineTransform.CreateGlobal(transform), true);
        }
    }
}