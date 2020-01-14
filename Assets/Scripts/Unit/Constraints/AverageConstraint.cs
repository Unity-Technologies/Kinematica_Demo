using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using UnityEngine.Animations;
using UnityEngine.Assertions;

namespace Unit
{
    public class AverageConstraint : MonoBehaviour, Constraint
    {
        [BurstCompile]
        internal struct Job : IAnimationJob, System.IDisposable
        {
            NativeArray<TransformStreamHandle> startTransforms;
            NativeArray<TransformStreamHandle> endTransforms;
            NativeArray<TransformStreamHandle> currentTransforms;

            public void Setup(Animator animator, AverageConstraint[] constraints)
            {
                int count = constraints.Length;

                startTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                endTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                currentTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < count; ++i)
                {
                    Assert.IsTrue(constraints[i].startTransform != null);
                    Assert.IsTrue(constraints[i].endTransform != null);
                    Assert.IsTrue(constraints[i].transform != null);

                    startTransforms[i] = animator.BindStreamTransform(constraints[i].startTransform);
                    endTransforms[i] = animator.BindStreamTransform(constraints[i].endTransform);
                    currentTransforms[i] = animator.BindStreamTransform(constraints[i].transform);
                }
            }

            public void Dispose()
            {
                startTransforms.Dispose();
                endTransforms.Dispose();
                currentTransforms.Dispose();
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                int count = currentTransforms.Length;
                for (int i = 0; i < count; ++i)
                {
                    currentTransforms[i].SetPosition(stream, Vector3.Lerp(startTransforms[i].GetPosition(stream), endTransforms[i].GetPosition(stream), 0.5f));
                    currentTransforms[i].SetRotation(stream, Quaternion.Lerp(startTransforms[i].GetRotation(stream), endTransforms[i].GetRotation(stream), 0.5f));
                }
            }

            public void ProcessRootMotion(AnimationStream stream) { }
        }

        public Transform startTransform;
        public Transform endTransform;

        public void Execute()
        {
            Vector3 t = Vector3.Lerp(startTransform.position, endTransform.position, 0.5f);
            Quaternion q = Quaternion.Slerp(startTransform.rotation, endTransform.rotation, 0.5f);

            transform.position = t;
            transform.rotation = q;
        }
    }
}