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

    public enum ClimbingState
    {
        Idle,
        Up,
        Down,
        Left,
        Right,
        UpLeft,
        UpRight,
        DownLeft,
        DownRight,
        CornerRight,
        CornerLeft
    }

    public enum Layer
    {
        Wall = 8
    }

    State state;
    State previousState;

    ClimbingState climbingState;
    ClimbingState previousClimbingState;

    FrameCapture capture;

    MemoryIdentifier transition;
    MemoryIdentifier locomotion;

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

        climbingState = ClimbingState.Idle;
        previousClimbingState = ClimbingState.Idle;

        clothComponents = GetComponentsInChildren<Cloth>();

        ledgeGeometry = LedgeGeometry.Create();
        wallGeometry = WallGeometry.Create();

        ledgeAnchor = LedgeAnchor.Create();
        wallAnchor = WallAnchor.Create();

        transition = MemoryIdentifier.Invalid;
        locomotion = MemoryIdentifier.Invalid;
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

                    bool freeClimbing = ledgeDistance >= 0.1f;

                    var climbingTrait = freeClimbing ?
                        Climbing.Create(Climbing.Type.Wall) :
                            Climbing.Create(Climbing.Type.Ledge);

                    if (freeClimbing)
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

                    SetClimbingState(ClimbingState.Idle);

                    locomotion = Push(ref synthesizer,
                        synthesizer.Query.Where(
                            climbingTrait).And(Idle.Default));
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
                UpdateFreeClimbing(
                    ref synthesizer, deltaTime);

                var desiredState = GetDesiredFreeClimbingState();

                if (!IsClimbingState(desiredState))
                {
                    var climbingTrait = Climbing.Create(Climbing.Type.Wall);

                    if (desiredState == ClimbingState.Idle)
                    {
                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.Down)
                    {
                        var direction = Direction.Create(Direction.Type.Down);

                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.UpRight)
                    {
                        var direction = Direction.Create(Direction.Type.UpRight);

                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.UpLeft)
                    {
                        var direction = Direction.Create(Direction.Type.UpLeft);

                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }

                    SetClimbingState(desiredState);
                }
                else
                {
                    synthesizer.Tick(locomotion);
                }

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
                synthesizer.Tick(locomotion);

                UpdateClimbing(
                    ref synthesizer, deltaTime);

                var desiredState = GetDesiredClimbingState();

                if (!IsClimbingState(desiredState))
                {
                    var climbingTrait = Climbing.Create(Climbing.Type.Ledge);

                    if (desiredState == ClimbingState.Idle)
                    {
                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.Right)
                    {
                        var direction = Direction.Create(Direction.Type.Right);

                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.Left)
                    {
                        var direction = Direction.Create(Direction.Type.Left);

                        locomotion = Push(ref synthesizer,
                            synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }

                    SetClimbingState(desiredState);
                }
                else
                {
                    synthesizer.Tick(locomotion);
                }

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

    public bool UseRootAsCameraFollow => false;

    void UpdateFreeClimbing(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        //
        // Smoothly adjust current root transform towards the anchor transform
        //

        AffineTransform deltaTransform =
            synthesizer.GetTrajectoryDeltaTransform(deltaTime);

        AffineTransform rootTransform =
            synthesizer.WorldRootTransform * deltaTransform;

        wallAnchor = wallGeometry.GetAnchor(rootTransform.t);

        float v = 1.0f - (2.8f / wallGeometry.GetHeight());
        wallAnchor.v = math.min(v, wallAnchor.v);

        float3 position = wallGeometry.GetPosition(wallAnchor);
        float distance = math.length(rootTransform.t - position);
        if (distance >= 0.01f)
        {
            float3 normal = math.normalize(position - rootTransform.t);
            rootTransform.t += normal * 0.5f * deltaTime;
        }
        rootTransform.t = position;

        float angle;
        float3 currentForward = Missing.zaxis(rootTransform.q);
        float3 desiredForward = -wallGeometry.GetNormalWorldSpace();
        quaternion q = Missing.forRotation(currentForward, desiredForward);
        float maximumAngle = math.radians(90.0f) * deltaTime;
        float3 axis = Missing.axisAngle(q, out angle);
        angle = math.min(angle, maximumAngle);
        rootTransform.q = math.mul(
            quaternion.AxisAngle(axis, angle), rootTransform.q);

        rootTransform *= deltaTransform.inverse();
        rootTransform.q = math.normalize(rootTransform.q);

        synthesizer.WorldRootTransform = rootTransform;

        wallGeometry.DebugDraw();
        wallGeometry.DebugDraw(ref wallAnchor);
    }

    ClimbingState GetDesiredFreeClimbingState()
    {
        float2 stickInput = GetStickInput();

        if (math.length(stickInput) >= 0.1f)
        {
            if (stickInput.y < stickInput.x)
            {
                return ClimbingState.Down;
            }

            if (stickInput.x > 0.0f)
            {
                return ClimbingState.UpRight;
            }

            return ClimbingState.UpLeft;
        }

        return ClimbingState.Idle;
    }

    ClimbingState GetDesiredClimbingState()
    {
        float2 stickInput = GetStickInput();

        if (math.length(stickInput) >= 0.1f)
        {
            if (stickInput.x > 0.0f)
            {
                return ClimbingState.Right;
            }

            return ClimbingState.Left;
        }

        return ClimbingState.Idle;
    }

    float2 GetStickInput()
    {
        float2 stickInput =
            new float2(capture.stickHorizontal,
                -capture.stickVertical);

        if (math.length(stickInput) >= 0.1f)
        {
            if (math.length(stickInput) > 1.0f)
                stickInput =
                    math.normalize(stickInput);

            return stickInput;
        }

        return float2.zero;
    }

    void UpdateClimbing(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        //
        // Smoothly adjust current root transform towards the anchor transform
        //

        AffineTransform deltaTransform =
            synthesizer.GetTrajectoryDeltaTransform(deltaTime);

        AffineTransform rootTransform =
            synthesizer.WorldRootTransform * deltaTransform;

        float linearDisplacement = -deltaTransform.t.x;

        ledgeAnchor = ledgeGeometry.UpdateAnchor(
            ledgeAnchor, linearDisplacement);

        float3 position = ledgeGeometry.GetPosition(ledgeAnchor);
        float distance = math.length(rootTransform.t - position);
        if (distance >= 0.01f)
        {
            float3 normal = math.normalize(position - rootTransform.t);
            rootTransform.t += normal * 0.5f * deltaTime;
        }
        rootTransform.t = position;

        float angle;
        float3 currentForward = Missing.zaxis(rootTransform.q);
        float3 desiredForward = ledgeGeometry.GetNormal(ledgeAnchor);
        quaternion q = Missing.forRotation(currentForward, desiredForward);
        float maximumAngle = math.radians(90.0f) * deltaTime;
        float3 axis = Missing.axisAngle(q, out angle);
        angle = math.min(angle, maximumAngle);
        rootTransform.q = math.mul(
            quaternion.AxisAngle(axis, angle), rootTransform.q);

        synthesizer.WorldRootTransform = rootTransform;

        ledgeGeometry.DebugDraw();
        ledgeGeometry.DebugDraw(ref ledgeAnchor);
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

    public void SetClimbingState(ClimbingState climbingState)
    {
        previousClimbingState = this.climbingState;
        this.climbingState = climbingState;
    }

    public bool IsClimbingState(ClimbingState climbingState)
    {
        return this.climbingState == climbingState;
    }

    public MemoryIdentifier Push(ref MotionSynthesizer synthesizer, QueryResult queryResult)
    {
        var sequence = synthesizer.Sequence();

        {
            sequence.Action().Push(queryResult);

            sequence.Action().Timer();
        }

        return sequence;
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
