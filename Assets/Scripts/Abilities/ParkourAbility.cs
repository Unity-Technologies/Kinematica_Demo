using Unity.Collections;
using Unity.Kinematica;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

using static TagExtensions;

using SnapshotProvider = Unity.SnapshotDebugger.SnapshotProvider;

[RequireComponent(typeof(AbilityRunner))]
[RequireComponent(typeof(MovementController))]
public partial class ParkourAbility : SnapshotProvider, Ability
{
    [Header("Transition settings")]
    [Tooltip("Distance in meters for performing movement validity checks.")]
    [Range(0.0f, 1.0f)]
    public float contactThreshold;

    [Header("Debug settings")]
    [Tooltip("Enables debug display for this ability.")]
    public bool enableDebugging;

    [Tooltip("Determines the movement to debug.")]
    public int debugIndex;

    [Tooltip("Controls the pose debug display.")]
    [Range(0, 100)]
    public int debugPoseIndex;

    SerializedInput capture;

    public static bool IsAxis(Collider collider, AffineTransform contactTransform, float3 axis)
    {
        float3 localNormal = Missing.rotateVector(
            Missing.conjugate(collider.transform.rotation),
                Missing.zaxis(contactTransform.q));

        return math.abs(math.dot(localNormal, axis)) >= 0.95f;
    }

    public override void OnEnable()
    {
        base.OnEnable();
    }

    public Ability OnUpdate(float deltaTime)
    {
        return this;
    }

    public bool OnContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, float deltaTime)
    {
        if (capture.jumpButton)
        {
            var controller = GetComponent<MovementController>();
            ref var closure = ref controller.current;
            Assert.IsTrue(closure.isColliding);

            var collider = closure.collider;

            int layerMask = 1 << collider.gameObject.layer;
            Assert.IsTrue((layerMask & 0x1F01) != 0);

            var type = Parkour.Create(collider.gameObject.layer);

            if (type.IsType(Parkour.Type.Wall) || type.IsType(Parkour.Type.Table))
            {
                if (IsAxis(collider, contactTransform, Missing.forward))
                {
                    return OnContact(ref synthesizer, contactTransform, deltaTime, type);
                }
            }
            else if (type.IsType(Parkour.Type.Platform))
            {
                if (IsAxis(collider, contactTransform, Missing.forward) ||
                    IsAxis(collider, contactTransform, Missing.right))
                {
                    return OnContact(ref synthesizer, contactTransform, deltaTime, type);
                }
            }
            else if (type.IsType(Parkour.Type.Ledge))
            {
                if (IsAxis(collider, contactTransform, Missing.right))
                {
                    return OnContact(ref synthesizer, contactTransform, deltaTime, type);
                }
            }
        }

        return false;
    }

    public bool OnDrop(ref MotionSynthesizer synthesizer, float deltaTime)
    {
        return false;
    }

    bool OnContact(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, float deltaTime, Parkour type)
    {
        OnContactDebug(ref synthesizer, contactTransform, type);

        //ref var shared = ref sharedData.Ref;

        //ref Binary binary = ref synthesizer.Binary;

        //var tags = GetAllTags(
        //    ref binary, contactTransform, tagIndices,
        //        payload, contactThreshold);

        //shared.settings =
        //    Transition.Settings.Create(ref binary, payload,
        //        tags, contactTransform, contactThreshold,
        //            maximumLinearError, maximumAngularError);

        //shared.deltaTime = deltaTime;

        //GetComponent<Animator>().AddJobDependency(
        //    Job.Create(sharedData).Schedule());

        return false;
    }

    void OnContactDebug(ref MotionSynthesizer synthesizer, AffineTransform contactTransform, Parkour type)
    {
        if (enableDebugging)
        {
            DisplayTransition(ref synthesizer, contactTransform, type, contactThreshold);
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
                    //if (AllContactsValid(ref binary, ref tag, obbs, contactThreshold))
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
            }

            obbs.Dispose();
        }
    }
}
