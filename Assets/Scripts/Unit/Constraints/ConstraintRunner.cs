using UnityEngine;

public class ConstraintRunner : MonoBehaviour
{
    public AnimationClip clip;

    [Range(0.0f, 1.0f)]
    public float fractionalTime;

    public bool autoPlay;

    private float sampleTimeInSeconds;

    Unit.AverageConstraint[] averageConstraints;
    Unit.LookAtConstraint[] lookAtConstraints;
    Unit.MuscleConstraint[] muscleConstraints;
    Unit.RotationConstraint[] rotationConstraints;

    void OnEnable()
    {
        averageConstraints = GetComponentsInChildren<Unit.AverageConstraint>();
        lookAtConstraints = GetComponentsInChildren<Unit.LookAtConstraint>();
        muscleConstraints = GetComponentsInChildren<Unit.MuscleConstraint>();
        rotationConstraints = GetComponentsInChildren<Unit.RotationConstraint>();
    }

    void Execute<T>(T[] constraints) where T : Unit.Constraint
    {
        int numConstraints = constraints.Length;
        for (int i = 0; i < numConstraints; ++i)
        {
            constraints[i].Execute();
        }
    }

    void Update()
    {
        if (clip != null)
        {
            clip.SampleAnimation(gameObject, sampleTimeInSeconds);

            if (autoPlay)
            {
                sampleTimeInSeconds += Time.deltaTime * 0.25f;
                if (sampleTimeInSeconds >= clip.length)
                {
                    sampleTimeInSeconds -= clip.length;
                }

                fractionalTime = sampleTimeInSeconds / clip.length;
            }
            else
            {
                sampleTimeInSeconds = clip.length * fractionalTime;
            }
        }
    }

    void LateUpdate()
    {
        Execute(averageConstraints);
        Execute(lookAtConstraints);
        Execute(muscleConstraints);
        Execute(rotationConstraints);
    }
}