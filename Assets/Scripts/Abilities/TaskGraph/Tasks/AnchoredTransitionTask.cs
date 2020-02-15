using Unity.Burst;
using Unity.Collections;
using Unity.Kinematica;
using Unity.Mathematics;

using UnityEngine;
using UnityEngine.Assertions;

using SegmentIndex = Unity.Kinematica.Binary.SegmentIndex;
using MarkerIndex = Unity.Kinematica.Binary.MarkerIndex;
using TypeIndex = Unity.Kinematica.Binary.TypeIndex;

[Data("AnchoredTransitionTask", "#2A3756"), BurstCompile]
public struct AnchoredTransitionTask : Task
{
    public MemoryRef<MotionSynthesizer> synthesizer;

    [Input("Candidates")]
    public Identifier<PoseSequence> sequences;

    public SamplingTime samplingTime;

    public AffineTransform contactTransform;

    public float maximumLinearError;
    public float maximumAngularError;

    public TimeIndex sourceTimeIndex;
    public TimeIndex targetTimeIndex;

    public enum State
    {
        Initializing,
        Waiting,
        Active,
        Complete
    }

    public State state;

    public void SetState(State state)
    {
        this.state = state;
    }

    public bool IsState(State state)
    {
        return this.state == state;
    }

    public bool IsComplete()
    {
        return IsState(State.Complete);
    }

    public Result Execute()
    {
        ref var synthesizer = ref this.synthesizer.Ref;

        if (IsState(State.Initializing))
        {
            if (FindTransition())
            {
                synthesizer.Push(targetTimeIndex);

                SetState(State.Waiting);
            }
            else
            {
                SetState(State.Complete);

                return Result.Failure;
            }
        }

        if (IsState(State.Waiting))
        {
            var samplingTime = synthesizer.Time;

            var frameIndex = GetEscapeFrameIndex();

            if (samplingTime.timeIndex.frameIndex >= frameIndex)
            {
                SetState(State.Complete);
            }
        }

        return Result.Success;
    }

    int GetEscapeFrameIndex()
    {
        Assert.IsTrue(targetTimeIndex.IsValid);

        ref var synthesizer = ref this.synthesizer.Ref;

        ref var binary = ref synthesizer.Binary;

        var escapeTypeIndex = binary.GetTypeIndex<Escape>();

        var samplingTime = synthesizer.Time;

        var escapeIndex = GetMarkerOfType(
            ref binary, samplingTime.segmentIndex, escapeTypeIndex);

        if (escapeIndex.IsValid)
        {
            return binary.GetMarker(escapeIndex).frameIndex;
        }

        ref var segment = ref binary.GetSegment(samplingTime.segmentIndex);

        var numFrames = segment.destination.numFrames - 1;

        return numFrames;
    }

    bool FindTransition()
    {
        ref var synthesizer = ref this.synthesizer.Ref;

        ref var binary = ref synthesizer.Binary;

        var sequences =
            synthesizer.GetArray<PoseSequence>(
                this.sequences);

        var sourceCandidates = CreateSourceCandidates();

        var numSourceCandidates = sourceCandidates.Length;

        var numCandidates = sequences.Length;

        for (int i=0; i<numCandidates; ++i)
        {
            var intervalIndex = sequences[i].intervalIndex;

            var segmentIndex =
                binary.GetInterval(
                    intervalIndex).segmentIndex;

            var targetCandidates =
                CreateTargetCandidates(segmentIndex);

            var numTargetCandidates = targetCandidates.Length;

            for (int j=0; j< numTargetCandidates; ++j)
            {
                var targetTransform = targetCandidates[j].worldRootTransform;

                var targetForward = Missing.zaxis(targetTransform.q);

                targetTimeIndex = targetCandidates[j].timeIndex;

                for (int k=0; k<numSourceCandidates; ++k)
                {
                    var sourceTransform = sourceCandidates[k].worldRootTransform;

                    var distance = sourceTransform.t - targetTransform.t;
                    
                    float linearError = math.length(distance);

                    if (linearError <= maximumLinearError)
                    {
                        var sourceForward = Missing.zaxis(sourceTransform.q);
                        float dot = math.dot(targetForward, sourceForward);
                        float clampedDot = math.clamp(dot, -1.0f, 1.0f);
                        float angularError = math.acos(clampedDot);

                        if (angularError <= maximumAngularError)
                        {
                            sourceTimeIndex = sourceCandidates[k].timeIndex;

                            break;
                        }
                    }
                }

                if (sourceTimeIndex.IsValid)
                {
                    break;
                }
            }

            targetCandidates.Dispose();
        }

        sourceCandidates.Dispose();

        return sourceTimeIndex.IsValid;
    }

    public struct SourceCandidate
    {
        public AffineTransform worldRootTransform;
        public TimeIndex timeIndex;

        public static SourceCandidate Create(AffineTransform worldRootTransform, TimeIndex timeIndex)
        {
            return new SourceCandidate
            {
                worldRootTransform = worldRootTransform,
                timeIndex = timeIndex
            };
        }
    }

    public NativeArray<SourceCandidate> CreateSourceCandidates()
    {
        ref var synthesizer = ref this.synthesizer.Ref;

        ref var binary = ref synthesizer.Binary;

        float timeHorizon = binary.TimeHorizon;
        float inverseSampleRate = 1.0f / binary.SampleRate;

        int numCandidates = Missing.truncToInt(timeHorizon / inverseSampleRate);

        AffineTransform previousTrajectoryTransform =
            binary.GetTrajectoryTransform(samplingTime);

        var worldRootTransform = synthesizer.WorldRootTransform;

        var candidates =
            new NativeArray<SourceCandidate>(
                numCandidates, Allocator.Temp);

        candidates[0] = SourceCandidate.Create(
            worldRootTransform, samplingTime.timeIndex);

        for (int i = 1; i < numCandidates; ++i)
        {
            samplingTime = binary.Advance(
                samplingTime, inverseSampleRate).samplingTime;

            AffineTransform currentTrajectoryTransform =
                binary.GetTrajectoryTransform(samplingTime);

            worldRootTransform *=
                previousTrajectoryTransform.inverseTimes(
                    currentTrajectoryTransform);

            previousTrajectoryTransform = currentTrajectoryTransform;

            candidates[i] = SourceCandidate.Create(
                worldRootTransform, samplingTime.timeIndex);
        }

        return candidates;
    }

    public struct TargetCandidate
    {
        public AffineTransform worldRootTransform;
        public TimeIndex timeIndex;

        public static TargetCandidate Create(AffineTransform worldRootTransform, TimeIndex timeIndex)
        {
            return new TargetCandidate
            {
                worldRootTransform = worldRootTransform,
                timeIndex = timeIndex
            };
        }
    }

    public NativeArray<TargetCandidate> CreateTargetCandidates(SegmentIndex segmentIndex)
    {
        ref var synthesizer = ref this.synthesizer.Ref;

        ref var binary = ref synthesizer.Binary;

        ref var segment = ref binary.GetSegment(segmentIndex);

        var anchorTypeIndex = binary.GetTypeIndex<Anchor>();

        var anchorIndex = GetMarkerOfType(
            ref binary, segmentIndex, anchorTypeIndex);
        Assert.IsTrue(anchorIndex.IsValid);

        ref var anchorMarker = ref binary.GetMarker(anchorIndex);

        var firstFrame = segment.destination.firstFrame;

        int anchorFrame = firstFrame + anchorMarker.frameIndex;

        AffineTransform anchorTransform =
            binary.GetPayload<Anchor>(anchorMarker.traitIndex).transform;

        AffineTransform anchorWorldSpaceTransform =
            contactTransform * anchorTransform;

        AffineTransform worldRootTransform = anchorWorldSpaceTransform *
            binary.GetTrajectoryTransformBetween(
                anchorFrame, -anchorMarker.frameIndex);

        var candidates =
            new NativeArray<TargetCandidate>(
                anchorMarker.frameIndex, Allocator.Temp);

        candidates[0] = TargetCandidate.Create(
            worldRootTransform, TimeIndex.Create(segmentIndex));

        AffineTransform previousTrajectoryTransform =
            binary.GetTrajectoryTransform(firstFrame);

        for (int i = 1; i < anchorMarker.frameIndex; ++i)
        {
            AffineTransform currentTrajectoryTransform =
                binary.GetTrajectoryTransform(firstFrame + i);

            worldRootTransform *=
                previousTrajectoryTransform.inverseTimes(
                    currentTrajectoryTransform);

            previousTrajectoryTransform = currentTrajectoryTransform;

            candidates[i] = TargetCandidate.Create(
                worldRootTransform, TimeIndex.Create(segmentIndex, i));
        }

        return candidates;
    }

    public static MarkerIndex GetMarkerOfType(ref Binary binary, SegmentIndex segmentIndex, TypeIndex typeIndex)
    {
        ref var segment = ref binary.GetSegment(segmentIndex);

        var numMarkers = segment.numMarkers;

        for (int i = 0; i < numMarkers; ++i)
        {
            var markerIndex = segment.markerIndex + i;

            if (binary.IsType(markerIndex, typeIndex))
            {
                return markerIndex;
            }
        }

        return MarkerIndex.Invalid;
    }

    [BurstCompile]
    public static Result ExecuteSelf(ref TaskRef self)
    {
        return self.Cast<AnchoredTransitionTask>().Execute();
    }

    public static AnchoredTransitionTask Create(ref MotionSynthesizer synthesizer, Identifier<PoseSequence> sequences, AffineTransform contactTransform, float maximumLinearError, float maximumAngularError)
    {
        return new AnchoredTransitionTask
        {
            synthesizer = synthesizer.self,
            sequences = sequences,
            contactTransform = contactTransform,
            maximumLinearError = maximumLinearError,
            maximumAngularError = maximumAngularError,
            sourceTimeIndex = TimeIndex.Invalid,
            targetTimeIndex = TimeIndex.Invalid,
            state = State.Initializing
        };
    }
}
