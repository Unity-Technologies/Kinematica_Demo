using System;
using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using UnityEngine.Animations;
using UnityEngine.Assertions;

namespace Unit
{
    public class LookAtConstraint : MonoBehaviour, Constraint
    {
        [BurstCompile]
        internal struct Job : IAnimationJob, System.IDisposable
        {
            NativeArray<TransformStreamHandle> targetTransforms;
            NativeArray<TransformStreamHandle> upAxisTransforms;
            NativeArray<TransformStreamHandle> currentTransforms;

            NativeArray<Vector3> lookAtAxes;
            NativeArray<Vector3> upAxes;

            public void Setup(Animator animator, LookAtConstraint[] constraints)
            {
                int count = constraints.Length;

                targetTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                upAxisTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                currentTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                lookAtAxes = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                upAxes = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                for (int i = 0; i < count; ++i)
                {
                    Assert.IsTrue(constraints[i].targetTransform != null);
                    Assert.IsTrue(constraints[i].upAxisTransform != null);
                    Assert.IsTrue(constraints[i].transform != null);

                    targetTransforms[i] = animator.BindStreamTransform(constraints[i].targetTransform);
                    upAxisTransforms[i] = animator.BindStreamTransform(constraints[i].upAxisTransform);
                    currentTransforms[i] = animator.BindStreamTransform(constraints[i].transform);

                    lookAtAxes[i] = Convert(constraints[i].lookAtAxis);
                    upAxes[i] = Convert(constraints[i].upAxis);
                }
            }

            public void Dispose()
            {
                targetTransforms.Dispose();
                upAxisTransforms.Dispose();
                currentTransforms.Dispose();
                lookAtAxes.Dispose();
                upAxes.Dispose();
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                int count = currentTransforms.Length;
                for (int i = 0; i < count; ++i)
                {
                    Vector3 lookAtDirection = targetTransforms[i].GetPosition(stream) - currentTransforms[i].GetPosition(stream);
                    Vector3 upAxis = upAxisTransforms[i].GetRotation(stream) * upAxes[i];

                    Quaternion q1 = Quaternion.LookRotation(lookAtDirection, upAxis);
                    Quaternion q2 = Quaternion.FromToRotation(Vector3.forward, lookAtAxes[i]);

                    currentTransforms[i].SetRotation(stream, q1 * Quaternion.Inverse(q2));
                }
            }

            public void ProcessRootMotion(AnimationStream stream) { }

            static Vector3 Convert(AxisDefinition axisDef)
            {
                if (axisDef.axis == Axis.X)
                    return Vector3.right * (axisDef.negative ? -1f : 1f);

                if (axisDef.axis == Axis.Y)
                    return Vector3.up * (axisDef.negative ? -1f : 1f);

                return Vector3.forward * (axisDef.negative ? -1f : 1f);
            }
        }

        public Transform targetTransform;
        public Transform upAxisTransform;

        public enum Axis
        {
            X, Y, Z
        }

        [Serializable]
        public struct AxisDefinition
        {
            public Axis axis;
            public bool negative;
        }

        public AxisDefinition lookAtAxis;
        public AxisDefinition upAxis;

        public void Execute()
        {
            var q1 = Quaternion.LookRotation(LookAtDirection, UpAxis);
            var q2 = Quaternion.FromToRotation(Vector3.forward, LookAtAxis);
            transform.rotation = q1 * Quaternion.Inverse(q2);

            //DrawTransform(0.3f);

            //Debug.DrawLine(transform.position, transform.position + UpAxis * 0.3f, Color.yellow);
        }

        public static Vector3 GetUnitAxis(Axis axis, bool negative)
        {
            if (axis == Axis.X)
            {
                return new Vector3(1.0f, 0.0f, 0.0f) * (negative ? -1.0f : 1.0f);
            }
            else if (axis == Axis.Y)
            {
                return new Vector3(0.0f, 1.0f, 0.0f) * (negative ? -1.0f : 1.0f);
            }
            else
            {
                return new Vector3(0.0f, 0.0f, 1.0f) * (negative ? -1.0f : 1.0f);
            }
        }

        public Vector3 LookAtDirection
        {
            get { return targetTransform.position - transform.position; }
        }

        public Vector3 LookAtAxis
        {
            get { return GetUnitAxis(lookAtAxis.axis, lookAtAxis.negative); }
        }

        public Vector3 UpAxis
        {
            get
            {
                return upAxisTransform.TransformDirection(
                    GetUnitAxis(upAxis.axis, upAxis.negative));
            }
        }

        public void DrawTransform(float scale)
        {
            Debug.DrawLine(transform.position, transform.position + transform.right * scale, Color.red);
            Debug.DrawLine(transform.position, transform.position + transform.up * scale, Color.green);
            Debug.DrawLine(transform.position, transform.position + transform.forward * scale, Color.blue);
        }
    }
}