using Unity.Kinematica;
using Unity.Mathematics;
using Unity.Collections;

using UnityEngine;

using static TagExtensions;

using SnapshotProvider = Unity.SnapshotDebugger.SnapshotProvider;

[RequireComponent(typeof(AbilityRunner))]
[RequireComponent(typeof(MovementController))]
public partial class ClimbingAbility : SnapshotProvider, Ability
{
    [Header("Transition settings")]
    [Tooltip("Distance in meters for performing movement validity checks.")]
    [Range(0.0f, 1.0f)]
    public float contactThreshold;

    [Tooltip("Maximum linear error for transition poses.")]
    [Range(0.0f, 1.0f)]
    public float maximumLinearError;

    [Tooltip("Maximum angular error for transition poses.")]
    [Range(0.0f, 180.0f)]
    public float maximumAngularError;

    [Header("Ledge prediction settings")]
    [Tooltip("Desired speed in meters per second for ledge climbing.")]
    [Range(0.0f, 10.0f)]
    public float desiredSpeedLedge;

    [Tooltip("How fast or slow the target velocity is supposed to be reached.")]
    [Range(0.0f, 1.0f)]
    public float velocityPercentageLedge;

    [Header("Climbing prediction settings")]
    [Tooltip("Desired speed in meters per second for free climbing.")]
    [Range(0.0f, 10.0f)]
    public float desiredSpeedClimbing;

    [Tooltip("How fast or slow the target velocity is supposed to be reached.")]
    [Range(0.0f, 1.0f)]
    public float velocityPercentageClimbing;

    [Header("Debug settings")]
    [Tooltip("Enables debug display for this ability.")]
    public bool enableDebugging;

    [Tooltip("Determines the movement to debug.")]
    public int debugIndex;

    [Tooltip("Controls the pose debug display.")]
    [Range(0, 100)]
    public int debugPoseIndex;

    public enum State
    {
        Suspended,
        Mounting,
        Climbing,
        FreeClimbing,
        Dismount,
        PullUp,
        DropDown
    }

    public enum Layer
    {
        Wall = 8
    }

    State state;
    State previousState;

    FrameCapture capture;

    MemoryIdentifier transition;

    Ledge.Type transitionType;

    LedgeGeometry ledgeGeometry;
    WallGeometry wallGeometry;

    LedgeAnchor ledgeAnchor;
    WallAnchor wallAnchor;

    Cloth[] clothComponents;

    public override void OnEnable()
    {
        base.OnEnable();

        state = State.Suspended;
        previousState = State.Suspended;

        clothComponents = GetComponentsInChildren<Cloth>();

        ledgeGeometry = LedgeGeometry.Create();
        wallGeometry = WallGeometry.Create();

        ledgeAnchor = LedgeAnchor.Create();
        wallAnchor = WallAnchor.Create();

        transition = MemoryIdentifier.Invalid;
    }

    public override void OnDisable()
    {
        base.OnDisable();

        ledgeGeometry.Dispose();
    }

    public Ability OnUpdate(float deltaTime)
    {
        var kinematica = GetComponent<Kinematica>();

        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        ConfigureController(!IsSuspended());

        ConfigureCloth(!IsSuspended());

        if (!IsSuspended())
        {
            if (IsState(State.Mounting))
            {
                if (IsTransitionComplete())
                {
                    float3 rootPosition = synthesizer.WorldRootTransform.t;

                    ledgeAnchor =
                        ledgeGeometry.GetAnchor(
                            rootPosition);

                    float3 ledgePosition =
                        ledgeGeometry.GetPosition(
                            ledgeAnchor);

                    float ledgeDistance = math.length(
                        rootPosition - ledgePosition);

                    if (ledgeDistance >= 0.1f)
                    {
                        wallAnchor =
                            wallGeometry.GetAnchor(
                                rootPosition);

                        SetState(State.FreeClimbing);
                    }
                    else
                    {
                        SetState(State.Climbing);
                    }
                }
            }
            else if (IsState(State.DropDown))
            {
                if (IsTransitionComplete())
                {
                    ledgeAnchor =
                        ledgeGeometry.GetAnchor(
                            synthesizer.WorldRootTransform.t);

                    SetState(State.Climbing);
                }
            }

            if (IsState(State.FreeClimbing))
            {
                //shared.displacementMagnitude =
                    //UpdateFreeClimbing(
                        //ref synthesizer, deltaTime);

                float height = wallGeometry.GetHeight(ref wallAnchor);
                float totalHeight = wallGeometry.GetHeight();
                bool closeToLedge = math.abs(totalHeight - height) <= 0.095f;
                bool closeToDrop = math.abs(height - 2.8f) <= 0.095f;

                if (closeToLedge && capture.stickVertical <= -0.9f)
                {
                    float3 rootPosition = synthesizer.WorldRootTransform.t;

                    ledgeAnchor =
                        ledgeGeometry.GetAnchor(
                            rootPosition);

                    float3 ledgePosition =
                        ledgeGeometry.GetPosition(
                            ledgeAnchor);

                    SetState(State.Climbing);
                }
                else if (closeToDrop && capture.stickVertical >= 0.9f)
                {
                    RequestDismountTransition(ref synthesizer, deltaTime);

                    SetState(State.Dismount);
                }
            }

            if (IsState(State.Climbing))
            {
                //displacementMagnitude =
                //UpdateClimbing(
                //ref synthesizer, deltaTime);

                AffineTransform rootTransform = synthesizer.WorldRootTransform;
                wallGeometry.Initialize(rootTransform);
                wallAnchor = wallGeometry.GetAnchor(rootTransform.t);
                float height = wallGeometry.GetHeight(ref wallAnchor);
                float totalHeight = wallGeometry.GetHeight();
                bool closeToDrop = math.abs(height - 2.8f) <= 0.05f;

                if (capture.pullUpButton)
                {
                    AffineTransform contactTransform =
                        ledgeGeometry.GetTransform(ledgeAnchor);

                    RequestPullUpTransition(ref synthesizer, contactTransform);

                    SetState(State.PullUp);
                }
                else if (capture.dismountButton && !closeToDrop)
                {
                    SetState(State.FreeClimbing);
                }
                else if (capture.dismountButton && closeToDrop)
                {
                    RequestDismountTransition(ref synthesizer, deltaTime);

                    SetState(State.Dismount);
                }
            }

            if (IsState(State.Dismount) || IsState(State.PullUp))
            {
                if (IsTransitionComplete())
                {
                    SetState(State.Suspended);
                }
            }

            return this;
        }

        return null;
    }

    public bool IsTransitionComplete()
    {
        bool active = transition.IsValid;

        if (active)
        {
            var kinematica = GetComponent<Kinematica>();

            ref var synthesizer = ref kinematica.Synthesizer.Ref;

            ref var transitionTask =
                ref synthesizer.GetByType<AnchoredTransitionTask>(
                    synthesizer.Root).Ref;

            if (!transitionTask.IsComplete())
            {
                synthesizer.Tick(transition);

                return false;
            }

            transition = MemoryIdentifier.Invalid;
        }

        return true;
    }

    public bool OnContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, float deltaTime)
    {
        if (capture.mountButton)
        {
            if (IsState(State.Suspended))
            {
                MovementController controller = GetComponent<MovementController>();
                BoxCollider collider = controller.current.collider as BoxCollider;

                if (collider != null)
                {
                    if (collider.gameObject.layer == (int)Layer.Wall)
                    {
                        ledgeGeometry.Initialize(collider);
                        wallGeometry.Initialize(collider, contactTransform);

                        RequestMountTransition(
                            ref synthesizer, collider,
                                contactTransform, deltaTime);

                        SetState(State.Mounting);

                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool OnDrop(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        return false;
    }

    public void RequestPullUpTransition(ref MotionSynthesizer synthesizer, AffineTransform contactTransform)
    {
        ref Binary binary = ref synthesizer.Binary;

        var action = synthesizer.Action();

        var trait = Ledge.Create(Ledge.Type.PullUp);

        var sequence = action.QueryResult(
            GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold));

        synthesizer.Allocate(
            AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError, false), action.self);

        transition = action.self;

        synthesizer.BringToFront(action.self);

        transitionType = Ledge.Type.PullUp;

        if (enableDebugging)
        {
            DisplayTransition(ref synthesizer,
                contactTransform, trait,
                    contactThreshold);
        }
    }

    public void RequestMountTransition(ref MotionSynthesizer synthesizer, BoxCollider collider, AffineTransform contactTransform, float deltaTime)
    {
        ref Binary binary = ref synthesizer.Binary;

        var action = synthesizer.Action();

        var trait = Ledge.Create(Ledge.Type.Mount);

        var sequence = action.QueryResult(
            GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold));

        synthesizer.Allocate(
            AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError), action.self);

        transition = action.self;

        synthesizer.BringToFront(action.self);

        transitionType = Ledge.Type.Mount;

        if (enableDebugging)
        {
            DisplayTransition(ref synthesizer,
                contactTransform, trait,
                    contactThreshold);
        }
    }

    void RequestDismountTransition(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        ref Binary binary = ref synthesizer.Binary;

        var action = synthesizer.Action();

        var trait = Ledge.Create(Ledge.Type.Dismount);

        AffineTransform contactTransform = synthesizer.WorldRootTransform;

        var sequence = action.QueryResult(
            GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold));

        synthesizer.Allocate(
            AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError, false), action.self);

        transition = action.self;

        synthesizer.BringToFront(action.self);

        transitionType = Ledge.Type.Dismount;

        if (enableDebugging)
        {
            DisplayTransition(ref synthesizer,
                contactTransform, trait,
                    contactThreshold);
        }
    }

    void RequestDropDownTransition(ref MotionSynthesizer synthesizer, AffineTransform contactTransform)
    {
        ref Binary binary = ref synthesizer.Binary;

        var action = synthesizer.Action();

        var trait = Ledge.Create(Ledge.Type.DropDown);

        var sequence = action.QueryResult(
            GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold));

        synthesizer.Allocate(
            AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError), action.self);

        transition = action.self;

        synthesizer.BringToFront(action.self);

        transitionType = Ledge.Type.DropDown;

        if (enableDebugging)
        {
            DisplayTransition(ref synthesizer,
                contactTransform, trait,
                    contactThreshold);
        }
    }

    void DisplayTransition<T>(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, T value, float contactThreshold) where T : struct
    {
        if (enableDebugging)
        {
            ref Binary binary = ref synthesizer.Binary;

            NativeArray<OBB> obbs =
                GetBoundsFromContactPoints(ref binary,
                    contactTransform, value, contactThreshold);

            //
            // Display all relevant box colliders
            //

            int numObbs = obbs.Length;
            for (int i = 0; i < numObbs; ++i)
            {
                OBB obb = obbs[i];
                obb.transform = contactTransform * obb.transform;
                DebugDraw(obb, Color.cyan);
            }

            var tagTraitIndex = binary.GetTraitIndex(value);

            int numTags = binary.numTags;

            int validIndex = 0;

            for (int i = 0; i < numTags; ++i)
            {
                ref Binary.Tag tag = ref binary.GetTag(i);

                if (tag.traitIndex == tagTraitIndex)
                {
                    if (validIndex == debugIndex)
                    {
                        DebugDrawContacts(ref binary, ref tag,
                            contactTransform, obbs, contactThreshold);

                        DebugDrawPoseAndTrajectory(ref binary, ref tag,
                            contactTransform, debugPoseIndex);

                        return;
                    }

                    validIndex++;
                }
            }

            obbs.Dispose();
        }
    }

    public void SetState(State newState)
    {
        previousState = state;
        state = newState;
    }

    public bool IsState(State queryState)
    {
        return state == queryState;
    }

    private bool WasState(State queryState)
    {
        if (previousState == queryState)
        {
            previousState = state;
            return true;
        }
        return false;
    }

    public bool IsSuspended()
    {
        return IsState(State.Suspended);
    }

    void ConfigureController(bool active)
    {
        var controller = GetComponent<MovementController>();

        controller.collisionEnabled = !active;
        controller.groundSnap = !active;
        controller.resolveGroundPenetration = !active;
        controller.gravityEnabled = !active;
    }

    void ConfigureCloth(bool active)
    {
        float worldVelocityScale = active ? 0.0f : 0.5f;

        int numClothComponents = clothComponents.Length;
        for (int i = 0; i < numClothComponents; ++i)
        {
            clothComponents[i].worldVelocityScale = worldVelocityScale;
        }
    }
}
