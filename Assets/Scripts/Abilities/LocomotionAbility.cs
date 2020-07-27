using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Kinematica;
using Unity.Mathematics;
using Unity.SnapshotDebugger;
using UnityEngine;
using UnityEngine.Assertions;

using SnapshotProvider = Unity.SnapshotDebugger.SnapshotProvider;

[BurstCompile(CompileSynchronously = true)]
public struct LocomotionJob : IJob
{
    public MemoryRef<MotionSynthesizer> synthesizer;

    public PoseSet idlePoses;

    public PoseSet locomotionPoses;

    public Trajectory trajectory;

    public bool idle;

    public float minTrajectoryDeviation;

    public float responsiveness;

    ref MotionSynthesizer Synthesizer => ref synthesizer.Ref;

    public void Execute()
    {
        if (idle && Synthesizer.MatchPose(idlePoses, Synthesizer.Time, MatchOptions.DontMatchIfCandidateIsPlaying | MatchOptions.LoopSegment, 0.01f))
        {
            return;
        }

        Synthesizer.MatchPoseAndTrajectory(locomotionPoses, Synthesizer.Time, trajectory, MatchOptions.None, responsiveness, minTrajectoryDeviation);
    }
}

[RequireComponent(typeof(AbilityRunner))]
[RequireComponent(typeof(MovementController))]
public partial class LocomotionAbility : SnapshotProvider, Ability, AbilityAnimatorMove
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
    public float responsiveness = 0.45f;

    [Tooltip("Speed in meters per second at which the character is considered to be braking (assuming player release the stick).")]
    [Range(0.0f, 10.0f)]
    public float brakingSpeed = 0.4f;

    [Header("Motion correction")]
    [Tooltip("How much root motion distance should be corrected to match desired trajectory.")]
    [Range(0.0f, 1.0f)]
    public float correctTranslationPercentage = 0.0f;

    [Tooltip("How much root motion rotation should be corrected to match desired trajectory.")]
    [Range(0.0f, 1.0f)]
    public float correctRotationPercentage = 1.0f;

    [Tooltip("Minimum character move speed (m/s) before root motion correction is applied.")]
    [Range(0.0f, 10.0f)]
    public float correctMotionStartSpeed = 2.0f;

    [Tooltip("Character move speed (m/s) at which root motion correction is fully effective.")]
    [Range(0.0f, 10.0f)]
    public float correctMotionEndSpeed = 3.0f;


    Kinematica kinematica;

    PoseSet idleCandidates;
    PoseSet locomotionCandidates;
    Trajectory trajectory;

    [Snapshot]
    float3 movementDirection = Missing.forward;

    [Snapshot]
    float moveIntensity = 0.0f;

    [Snapshot]
    bool run;

    [Snapshot]
    bool isBraking = false;

    [Snapshot]
    float3 rootVelocity = float3.zero;

    float desiredLinearSpeed => run ? desiredSpeedFast : desiredSpeedSlow;



    struct SamplingTimeInfo
    {
        public bool isLocomotion;
        public bool hasReachedEndOfSegment;
    }

    public override void OnEnable()
    {
        base.OnEnable();

        kinematica = GetComponent<Kinematica>();
        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        idleCandidates = synthesizer.Query.Where("Idle", Locomotion.Default).And(Idle.Default);
        locomotionCandidates = synthesizer.Query.Where("Locomotion", Locomotion.Default).Except(Idle.Default);
        trajectory = synthesizer.CreateTrajectory(Allocator.Persistent);

        synthesizer.PlayFirstSequence(idleCandidates);

        rootVelocity = synthesizer.CurrentVelocity;
    }

    public override void OnDisable()
    {
        base.OnDisable();

        idleCandidates.Dispose();
        locomotionCandidates.Dispose();
        trajectory.Dispose();
    }

    public override void OnEarlyUpdate(bool rewind)
    {
        base.OnEarlyUpdate(rewind);

        if (!rewind)
        {
            Utility.GetInputMove(ref movementDirection, ref moveIntensity);
            run = Input.GetButton("A Button");
        }
    }

    public Ability OnUpdate(float deltaTime)
    {
        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        bool idle = moveIntensity == 0.0f;
        float desiredSpeed;
        if (idle)
        {
            desiredSpeed = 0.0f;

            if (!isBraking && math.length(synthesizer.CurrentVelocity) < brakingSpeed)
            {
                isBraking = true;
            }
        }
        else
        {
            isBraking = false;

            desiredSpeed = moveIntensity * desiredLinearSpeed;
        }

        // If character is braking, we set a strong deviation threshold on Trajectory Heuristic to be conservative (candidate would need to be a LOT better to be picked)
        // because then we want the character to pick a stop clip in the library and stick to it even if Kinematica can jump to a better clip (cost wise) in the middle 
        // of that stop animation. Indeed stop animations have very subtle foot steps (to reposition to idle stance) that would be squeezed by blend/jumping from clip to clip.
        // Moreover, playing a stop clip from start to end will make sure we will reach a valid transition point to idle.
        SamplingTimeInfo samplingTimeInfo = GetSamplingTimeInfo();
        float minTrajectoryDeviation = 0.03f; // default threshold
        if (samplingTimeInfo.isLocomotion)
        {
            if (isBraking)
            {
                minTrajectoryDeviation = 0.25f; // high threshold to let stop animation finish
            }
        }
        else if (samplingTimeInfo.hasReachedEndOfSegment)
        {
            minTrajectoryDeviation = 0.0f; // we are not playing a locomotion segment and we reach the end of that segment, we must force a transition, otherwise character will freeze in the last position
        }
        
        var prediction = TrajectoryPrediction.CreateFromDirection(ref kinematica.Synthesizer.Ref,
                movementDirection,
                desiredSpeed,
                trajectory,
                velocityPercentage,
                forwardPercentage);

        var controller = GetComponent<MovementController>();

        Assert.IsTrue(controller != null);

        controller.Snapshot();

        var transform = prediction.Transform;

        var worldRootTransform = synthesizer.WorldRootTransform;

        float inverseSampleRate =
            Missing.recip(synthesizer.Binary.SampleRate);

        bool attemptTransition = true;

        Ability contactAbility = null;

        while (prediction.Push(transform))
        {
            transform = prediction.Advance;

            controller.MoveTo(worldRootTransform.transform(transform.t));
            controller.Tick(inverseSampleRate);

            ref var closure = ref controller.current;

            if (closure.isColliding && attemptTransition)
            {
                float3 contactPoint = closure.colliderContactPoint;
                contactPoint.y = controller.Position.y;

                float3 contactNormal = closure.colliderContactNormal;
                quaternion q = math.mul(transform.q,
                    Missing.forRotation(Missing.zaxis(transform.q),
                        contactNormal));

                AffineTransform contactTransform = new AffineTransform(contactPoint, q);

                if (contactAbility == null)
                {
                    foreach (Ability ability in GetComponents(typeof(Ability)))
                    {
                        if (ability.OnContact(ref synthesizer, contactTransform, deltaTime))
                        {
                            contactAbility = ability;
                            break;
                        }
                    }
                }

                attemptTransition = false;
            }
            else if (!closure.isGrounded)
            {
                if (contactAbility == null)
                {
                    foreach (Ability ability in GetComponents(typeof(Ability)))
                    {
                        if (ability.OnDrop(ref synthesizer, deltaTime))
                        {
                            contactAbility = ability;
                            break;
                        }
                    }
                }
            }

            transform.t =
                worldRootTransform.inverseTransform(
                    controller.Position);

            prediction.Transform = transform;
        }

        controller.Rewind();

        LocomotionJob job = new LocomotionJob()
        {
            synthesizer = kinematica.Synthesizer,
            idlePoses = idleCandidates,
            locomotionPoses = locomotionCandidates,
            trajectory = trajectory,
            idle = moveIntensity == 0.0f,
            minTrajectoryDeviation = minTrajectoryDeviation,
            responsiveness = responsiveness
        };

        kinematica.AddJobDependency(job.Schedule());

        if (contactAbility != null)
        {
            return contactAbility;
        }

        return this;
    }

    public bool UseRootAsCameraFollow => true;

    public bool OnContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, float deltaTime)
    {
        return false;
    }

    public bool OnDrop(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        return false;
    }

    public void OnAbilityAnimatorMove()
    {
        var kinematica = GetComponent<Kinematica>();
        if (kinematica.Synthesizer.IsValid)
        {
            ref MotionSynthesizer synthesizer = ref kinematica.Synthesizer.Ref;

            AffineTransform rootMotion = synthesizer.SteerRootMotion(trajectory, correctTranslationPercentage, correctRotationPercentage, correctMotionStartSpeed, correctMotionEndSpeed);
            AffineTransform rootTransform = AffineTransform.Create(transform.position, transform.rotation) * rootMotion;

            synthesizer.SetWorldTransform(AffineTransform.Create(rootTransform.t, rootTransform.q), true);

            if (synthesizer.deltaTime >= 0.0f)
            {
                rootVelocity = rootMotion.t / synthesizer.deltaTime;
            }
        }
    }

    SamplingTimeInfo GetSamplingTimeInfo()
    {
        SamplingTimeInfo samplingTimeInfo = new SamplingTimeInfo()
        {
            isLocomotion = false,
            hasReachedEndOfSegment = false
        };

        var kinematica = GetComponent<Kinematica>();
        ref var synthesizer = ref kinematica.Synthesizer.Ref;
        ref Binary binary = ref synthesizer.Binary;

        SamplingTime samplingTime = synthesizer.Time;

        ref Binary.Segment segment = ref binary.GetSegment(samplingTime.timeIndex.segmentIndex);
        ref Binary.Tag tag = ref binary.GetTag(segment.tagIndex);
        ref Binary.Trait trait = ref binary.GetTrait(tag.traitIndex);

        if (trait.typeIndex == binary.GetTypeIndex<Locomotion>())
        {
            samplingTimeInfo.isLocomotion = true;
        }
        else if (samplingTime.timeIndex.frameIndex >= segment.destination.numFrames - 1)
        {
            samplingTimeInfo.hasReachedEndOfSegment = true;
        }

        return samplingTimeInfo;
    }
}
