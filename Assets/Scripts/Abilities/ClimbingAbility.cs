using Unity.Kinematica;
using Unity.Mathematics;
using Unity.Collections;

using UnityEngine;

using static TagExtensions;

using SnapshotProvider = Unity.SnapshotDebugger.SnapshotProvider;
using Unity.SnapshotDebugger;

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

    public struct FrameCapture
    {
        public float stickHorizontal;
        public float stickVertical;
        public bool mountButton;
        public bool dismountButton;
        public bool pullUpButton;

        public void Update()
        {
            stickHorizontal = Input.GetAxis("Left Analog Horizontal");
            stickVertical = Input.GetAxis("Left Analog Vertical");

            mountButton = Input.GetButton("B Button") || Input.GetKey("b");
            dismountButton = Input.GetButton("B Button") || Input.GetKey("b");
            pullUpButton = Input.GetButton("A Button") || Input.GetKey("a");
        }
    }

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
        CornerLeft,
        None,
    }

    public enum Layer
    {
        Wall = 8
    }

    Kinematica kinematica;

    State state;
    State previousState;

    ClimbingState climbingState;
    ClimbingState previousClimbingState;
    ClimbingState lastCollidingClimbingState;

    [Snapshot]
    FrameCapture capture;

    [Snapshot]
    LedgeGeometry ledgeGeometry;

    [Snapshot]
    WallGeometry wallGeometry;

    [Snapshot]
    LedgeAnchor ledgeAnchor;

    [Snapshot]
    WallAnchor wallAnchor;

    Cloth[] clothComponents;

    [Snapshot]
    AnchoredTransitionTask anchoredTransition;

    public override void OnEnable()
    {
        base.OnEnable();

        kinematica = GetComponent<Kinematica>();

        state = State.Suspended;
        previousState = State.Suspended;

        climbingState = ClimbingState.Idle;
        previousClimbingState = ClimbingState.Idle;
        lastCollidingClimbingState = ClimbingState.None;

        clothComponents = GetComponentsInChildren<Cloth>();

        ledgeGeometry = LedgeGeometry.Create();
        wallGeometry = WallGeometry.Create();

        ledgeAnchor = LedgeAnchor.Create();
        wallAnchor = WallAnchor.Create();

        anchoredTransition = AnchoredTransitionTask.Invalid;
    }

    public override void OnDisable()
    {
        base.OnDisable();

        ledgeGeometry.Dispose();
        anchoredTransition.Dispose();
    }

    public override void OnEarlyUpdate(bool rewind)
    {
        base.OnEarlyUpdate(rewind);

        if (!rewind)
        {
            capture.Update();
        }
    }

    public Ability OnUpdate(float deltaTime)
    {
        ref var synthesizer = ref kinematica.Synthesizer.Ref;

        ConfigureController(!IsSuspended());

        ConfigureCloth(!IsSuspended());

        if (!IsSuspended())
        {
            if (IsState(State.Mounting))
            {
                bool bTransitionSucceeded;
                if (IsTransitionComplete(out bTransitionSucceeded))
                {
                    if (!bTransitionSucceeded)
                    {
                        SetState(State.Suspended);
                        return null;
                    }

                    float3 rootPosition = synthesizer.WorldRootTransform.t;

                    ledgeAnchor =
                        ledgeGeometry.GetAnchor(
                            rootPosition);

                    float3 ledgePosition =
                        ledgeGeometry.GetPosition(
                            ledgeAnchor);

                    float ledgeDistance = math.length(
                        rootPosition - ledgePosition);

                    bool freeClimbing = false; // ledgeDistance >= 0.1f;

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

                    PlayFirstSequence(synthesizer.Query.Where(climbingTrait).And(Idle.Default));
                }
            }
            else if (IsState(State.DropDown))
            {
                bool bTransitionSucceeded;
                if (IsTransitionComplete(out bTransitionSucceeded))
                {
                    if (!bTransitionSucceeded)
                    {
                        SetState(State.Suspended);
                        return null;
                    }

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
                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.Down)
                    {
                        var direction = Direction.Create(Direction.Type.Down);

                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.UpRight)
                    {
                        var direction = Direction.Create(Direction.Type.UpRight);

                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.UpLeft)
                    {
                        var direction = Direction.Create(Direction.Type.UpLeft);

                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }

                    SetClimbingState(desiredState);
                }


                float height = wallGeometry.GetHeight(ref wallAnchor);
                float totalHeight = wallGeometry.GetHeight();
                bool closeToLedge = math.abs(totalHeight - height) <= 0.095f;
                bool closeToDrop = math.abs(height - 2.8f) <= 0.095f;

                if (closeToLedge && capture.stickVertical >= 0.9f)
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
                else if (closeToDrop && capture.stickVertical <= -0.9f)
                {
                    RequestDismountTransition(ref synthesizer, deltaTime);

                    SetState(State.Dismount);
                }
            }

            if (IsState(State.Climbing))
            {
                UpdateClimbing(
                    ref synthesizer, deltaTime);

                var desiredState = GetDesiredClimbingState();
                if (desiredState == lastCollidingClimbingState)
                {
                    desiredState = ClimbingState.Idle;
                }

                if (!IsClimbingState(desiredState))
                {
                    var climbingTrait = Climbing.Create(Climbing.Type.Ledge);

                    if (desiredState == ClimbingState.Idle)
                    {
                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.Right)
                    {
                        var direction = Direction.Create(Direction.Type.Right);

                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }
                    else if (desiredState == ClimbingState.Left)
                    {
                        var direction = Direction.Create(Direction.Type.Left);

                        PlayFirstSequence(synthesizer.Query.Where(
                                climbingTrait).And(direction).Except(Idle.Default));
                    }

                    SetClimbingState(desiredState);
                }

                AffineTransform rootTransform = synthesizer.WorldRootTransform;
                wallGeometry.Initialize(rootTransform);
                wallAnchor = wallGeometry.GetAnchor(rootTransform.t);
                float height = wallGeometry.GetHeight(ref wallAnchor);
                float totalHeight = wallGeometry.GetHeight();
                bool closeToDrop = math.abs(height - 2.8f) <= 0.05f;

                if (capture.pullUpButton && CanPullUp())
                {
                    AffineTransform contactTransform =
                        ledgeGeometry.GetTransform(ledgeAnchor);

                    RequestPullUpTransition(ref synthesizer, contactTransform);

                    SetState(State.PullUp);
                }
                else if (capture.dismountButton /*&& closeToDrop*/)
                {
                    RequestDismountTransition(ref synthesizer, deltaTime);

                    SetState(State.Dismount);
                }
            }

            if (IsState(State.Dismount) || IsState(State.PullUp) || IsState(State.DropDown))
            {
                bool bTransitionSucceeded;
                if (IsTransitionComplete(out bTransitionSucceeded))
                {
                    SetState(State.Suspended);
                }
            }

            if (anchoredTransition.isValid)
            {
                if (!anchoredTransition.IsState(AnchoredTransitionTask.State.Complete) && !anchoredTransition.IsState(AnchoredTransitionTask.State.Failed))
                {
                    anchoredTransition.synthesizer = MemoryRef<MotionSynthesizer>.Create(ref synthesizer);
                    kinematica.AddJobDependency(AnchoredTransitionJob.Schedule(ref anchoredTransition));
                }
                else
                {
                    anchoredTransition.Dispose();
                }
            }

            return this;
        }

        return null;
    }

    public bool IsTransitionComplete(out bool bSuccess)
    {
        bSuccess = false;
        bool active = anchoredTransition.isValid;

        if (active)
        {
            ref var synthesizer = ref kinematica.Synthesizer.Ref;

            if (anchoredTransition.IsState(AnchoredTransitionTask.State.Complete))
            {
                anchoredTransition.Dispose();
                anchoredTransition = AnchoredTransitionTask.Invalid;
                bSuccess = true;
                return true;
            }
            else if (anchoredTransition.IsState(AnchoredTransitionTask.State.Failed))
            {
                anchoredTransition.Dispose();
                anchoredTransition = AnchoredTransitionTask.Invalid;
                return true;
            }
            else
            {
                return false;
            }
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

        if (math.abs(stickInput.x) >= 0.5f)
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
                capture.stickVertical);

        if (math.length(stickInput) >= 0.1f)
        {
            if (math.length(stickInput) > 1.0f)
                stickInput =
                    math.normalize(stickInput);

            return stickInput;
        }

        return float2.zero;
    }

    bool UpdateCollidingClimbingState(float desiredMoveOnLedge, float3 desiredPosition, float3 desiredForward)
    {
        bool bCollision = IsCharacterCapsuleColliding(desiredPosition - math.normalize(desiredForward) * 0.5f - new float3(0.0f, 1.5f, 0.0f));

        if (climbingState == ClimbingState.Idle)
        {
            lastCollidingClimbingState = ClimbingState.None;
        }
        else if (bCollision)
        {
            float currentMoveDirection = climbingState == ClimbingState.Left ? 1.0f : -1.0f;
            if (currentMoveDirection * desiredMoveOnLedge > 0.0f)
            {
                lastCollidingClimbingState = climbingState;
            }
        }

        return bCollision;
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

        LedgeAnchor desiredLedgeAnchor = ledgeGeometry.UpdateAnchor(
            ledgeAnchor, linearDisplacement);

        float3 position = ledgeGeometry.GetPosition(desiredLedgeAnchor);
        float3 desiredForward = ledgeGeometry.GetNormal(desiredLedgeAnchor);

        if (!UpdateCollidingClimbingState(linearDisplacement, position, desiredForward))
        {
            ledgeAnchor = desiredLedgeAnchor;
        }

        float distance = math.length(rootTransform.t - position);
        if (distance >= 0.01f)
        {
            float3 normal = math.normalize(position - rootTransform.t);
            rootTransform.t += normal * 0.5f * deltaTime;
        }
        rootTransform.t = position;

        float angle;
        float3 currentForward = Missing.zaxis(rootTransform.q);
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

        var trait = Ledge.Create(Ledge.Type.PullUp);

        var sequence = GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold);

        anchoredTransition.Dispose();
        anchoredTransition = AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError, false);

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

        var trait = Ledge.Create(Ledge.Type.Mount);

        var sequence = GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold);

        anchoredTransition.Dispose();
        anchoredTransition = AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError);

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

        var trait = Ledge.Create(Ledge.Type.Dismount);

        AffineTransform contactTransform = synthesizer.WorldRootTransform;

        var sequence = GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold);

        anchoredTransition.Dispose();
        anchoredTransition = AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError, false);

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

        var trait = Ledge.Create(Ledge.Type.DropDown);

        var sequence = GetPoseSequence(ref binary, contactTransform,
                trait, contactThreshold);

        anchoredTransition.Dispose();
        anchoredTransition = AnchoredTransitionTask.Create(ref synthesizer,
                sequence, contactTransform, maximumLinearError,
                    maximumAngularError, false);

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
        lastCollidingClimbingState = ClimbingState.None;
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

    public void PlayFirstSequence(PoseSet poses)
    {
        kinematica.Synthesizer.Ref.PlayFirstSequence(poses);
        poses.Dispose();
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

    bool CanPullUp()
    {
        return !IsCharacterCapsuleColliding(transform.position);
    }

    bool IsCharacterCapsuleColliding(Vector3 rootPosition)
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        Vector3 capsuleCenter = rootPosition + capsule.center;
        Vector3 capsuleOffset = Vector3.up * (capsule.height * 0.5f - capsule.radius);

        return Physics.CheckCapsule(capsuleCenter - capsuleOffset, capsuleCenter + capsuleOffset, capsule.radius - 0.1f, EnvironmentCollisionMask);
    }
}
