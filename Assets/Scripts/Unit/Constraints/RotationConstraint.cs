using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Burst;
using UnityEngine.Animations;
using UnityEngine.Assertions;

namespace Unit
{
    public class RotationConstraint : MonoBehaviour, Constraint
    {
        [BurstCompile]
        internal struct Job : IAnimationJob, System.IDisposable
        {
            NativeArray<TransformStreamHandle> referenceTransforms;
            NativeArray<TransformStreamHandle> currentTransforms;

            NativeArray<Vector2> minMaxSpeeds;
            NativeArray<Vector2> minMaxAngles;

            NativeArray<Vector3> lastPositions;
            NativeArray<Quaternion> initialRotations;
            NativeArray<float> lastAngles;

            PropertySceneHandle deltaTime;

            public void Setup(Animator animator, RotationConstraint[] constraints)
            {
                int count = constraints.Length;

                referenceTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                currentTransforms = new NativeArray<TransformStreamHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                minMaxSpeeds = new NativeArray<Vector2>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                minMaxAngles = new NativeArray<Vector2>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                lastPositions = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                initialRotations = new NativeArray<Quaternion>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                lastAngles = new NativeArray<float>(count, Allocator.Persistent);

                for (int i = 0; i < count; ++i)
                {
                    Assert.IsTrue(constraints[i].referenceTransform != null);
                    Assert.IsTrue(constraints[i].transform != null);

                    referenceTransforms[i] = animator.BindStreamTransform(constraints[i].referenceTransform);
                    currentTransforms[i] = animator.BindStreamTransform(constraints[i].transform);

                    minMaxSpeeds[i] = new Vector2(constraints[i].minimumSpeed, constraints[i].maximumSpeed);
                    minMaxAngles[i] = new Vector2(constraints[i].minimumAngle, constraints[i].maximumAngle);

                    lastPositions[i] = constraints[i].referenceTransform.position;
                    initialRotations[i] = constraints[i].transform.localRotation;
                }

                deltaTime = animator.BindSceneProperty(animator.gameObject.transform, typeof(Unit), "m_deltaTime");
            }

            public void Dispose()
            {
                referenceTransforms.Dispose();
                currentTransforms.Dispose();
                minMaxSpeeds.Dispose();
                minMaxAngles.Dispose();
                lastPositions.Dispose();
                initialRotations.Dispose();
                lastAngles.Dispose();
            }

            public void ProcessAnimation(AnimationStream stream)
            {
                var deltaTime = this.deltaTime.GetFloat(stream);

                if (deltaTime > 0.0f)
                {
                    int count = currentTransforms.Length;
                    for (int i = 0; i < count; ++i)
                    {
                        Vector3 deltaPosition = referenceTransforms[i].GetPosition(stream) - lastPositions[i];
                        lastPositions[i] = referenceTransforms[i].GetPosition(stream);

                        float linearSpeedInMetersPerSecond = deltaPosition.magnitude / deltaTime;

                        float theta = Mathf.Clamp(
                            (linearSpeedInMetersPerSecond - minMaxSpeeds[i].x) /
                                (minMaxSpeeds[i].y - minMaxSpeeds[i].x), 0f, 1f);

                        float targetAngle = Mathf.Lerp(minMaxAngles[i].x, minMaxAngles[i].y, theta);
                        float angle = Mathf.Lerp(lastAngles[i], targetAngle, 0.25f);
                        lastAngles[i] = angle;

                        currentTransforms[i].SetLocalRotation(stream, initialRotations[i] * Quaternion.AngleAxis(angle, Vector3.forward));
                    }
                }
            }

            public void ProcessRootMotion(AnimationStream stream) { }
        }

        public Transform referenceTransform;

        public float minimumSpeed;
        public float maximumSpeed;
        public float minimumAngle;
        public float maximumAngle;

        Vector3 lastPosition;
        Quaternion initialRotation;
        float lastAngle;

        public void Start()
        {
            lastPosition = referenceTransform.position;

            initialRotation = transform.localRotation;
        }

        public void Execute()
        {
            Vector3 deltaPosition = referenceTransform.position - lastPosition;
            lastPosition = referenceTransform.position;

            float linearSpeedInMetersPerSecond = deltaPosition.magnitude / Time.deltaTime;

            float theta = math.saturate(
                (linearSpeedInMetersPerSecond - minimumSpeed) /
                    (maximumSpeed - minimumSpeed));

            float targetAngle = math.lerp(minimumAngle, maximumAngle, theta);
            float angle = math.lerp(lastAngle, targetAngle, 0.25f);
            lastAngle = angle;

            transform.localRotation = initialRotation * Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }
}