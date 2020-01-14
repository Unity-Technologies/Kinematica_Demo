using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using UnityEngine.Animations;
using UnityEngine.Assertions;

namespace Unit
{
    public class MuscleConstraint : MonoBehaviour, Constraint
    {
        [BurstCompile]
        internal struct Job : IAnimationJob, System.IDisposable
        {
            NativeArray<TransformStreamHandle> startTransforms;
            NativeArray<TransformStreamHandle> endTransforms;
            NativeArray<TransformStreamHandle> currentTransforms;

            NativeArray<float> referenceLengths;
            NativeArray<float> multipliers;

            public void Setup(Animator animator, MuscleConstraint[] constraints)
            {
                int count = constraints.Length;

                startTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                endTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                currentTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                referenceLengths = new NativeArray<float>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                multipliers = new NativeArray<float>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < count; ++i)
                {
                    Assert.IsTrue(constraints[i].startTransform != null);
                    Assert.IsTrue(constraints[i].endTransform != null);
                    Assert.IsTrue(constraints[i].transform != null);

                    startTransforms[i] = animator.BindStreamTransform(constraints[i].startTransform);
                    endTransforms[i] = animator.BindStreamTransform(constraints[i].endTransform);
                    currentTransforms[i] = animator.BindStreamTransform(constraints[i].transform);

                    referenceLengths[i] = constraints[i].referenceLength;
                    multipliers[i] = constraints[i].multiplier;
                }
            }

            public void Dispose()
            {
                startTransforms.Dispose();
                endTransforms.Dispose();
                currentTransforms.Dispose();
                referenceLengths.Dispose();
                multipliers.Dispose();
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                int count = currentTransforms.Length;
                for (int i = 0; i < count; ++i)
                {
                    float length = (startTransforms[i].GetPosition(stream) - endTransforms[i].GetPosition(stream)).magnitude;

                    float ratio = length / referenceLengths[i];
                    ratio = ratio * multipliers[i] - multipliers[i] + 1.0f;
                    ratio = Mathf.Clamp(ratio, 0.5f, 2.0f);

                    float invRatio = 1.0f / ratio;
                    currentTransforms[i].SetLocalScale(stream, new Vector3(ratio, invRatio, invRatio));
                }
            }

            public void ProcessRootMotion(AnimationStream stream) { }
        }

        public Transform startTransform;
        public Transform endTransform;
        public float referenceLength;
        public float multiplier;

        public void Execute()
        {
            float length =
                (startTransform.position -
                    endTransform.position).magnitude;

            // float ratio = math.clamp(length / referenceLength, 0.5f, 2.0f);
            float ratio = length / referenceLength;
            ratio = ratio * multiplier - multiplier + 1.0f;
            ratio = math.clamp(ratio, 0.5f, 2.0f);

            float inverseRatio = 1.0f / ratio;

            transform.localScale =
                new Vector3(ratio, inverseRatio,
                    inverseRatio);
        }
    }
}