using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;

using Unity.Kinematica;

using Unity;
using System;

namespace Unit
{
    public class Unit : AbilityRunner
    {
        PlayableGraph m_Graph;

        AverageConstraint.Job averageConstraintJob;
        MuscleConstraint.Job muscleConstraintJob;
        LookAtConstraint.Job lookAtConstraintJob;
        RotationConstraint.Job rotationConstraintJob;

        [SerializeField]
        [HideInInspector]
        private float m_deltaTime;

        public override void OnEnable()
        {
            base.OnEnable();

            CreatePlayableGraph();

            var kinematica = GetComponent<Kinematica>();

            ref var synthesizer = ref kinematica.Synthesizer.Ref;

            using (PoseSet idlePoses = synthesizer.Query.Where(Locomotion.Default).And(Idle.Default))
            {
                synthesizer.PlayFirstSequence(idlePoses);
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();

            DestroyPlayableGraph();
        }

        public override void Update()
        {
            m_deltaTime = Time.deltaTime;

            base.Update();
        }

        void CreatePlayableGraph()
        {
            var animator = GetComponent<Animator>();

            string graphName = "UnityPlayableGraph_" + animator.transform.name;
            m_Graph = PlayableGraph.Create(graphName);

            var output = AnimationPlayableOutput.Create(m_Graph, "ouput", animator);

            // This line is saying to stack previous animator values as inputs to new playables
            output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);

            GetComponent<ConstraintSetup>().CreateConstraints();

            Constraint[] constraints = GetComponentsInChildren<Constraint>();

            averageConstraintJob = new AverageConstraint.Job();
            averageConstraintJob.Setup(animator, FilterConstraintsByType<AverageConstraint>(constraints));

            lookAtConstraintJob = new LookAtConstraint.Job();
            lookAtConstraintJob.Setup(animator, FilterConstraintsByType<LookAtConstraint>(constraints));

            muscleConstraintJob = new MuscleConstraint.Job();
            muscleConstraintJob.Setup(animator, FilterConstraintsByType<MuscleConstraint>(constraints));

            rotationConstraintJob = new RotationConstraint.Job();
            rotationConstraintJob.Setup(animator, FilterConstraintsByType<RotationConstraint>(constraints));

            var averageConstraintPlayable = AnimationScriptPlayable.Create(m_Graph, averageConstraintJob);
            var lookAtConstraintPlayable = AnimationScriptPlayable.Create(m_Graph, lookAtConstraintJob);
            var muscleConstraintPlayable = AnimationScriptPlayable.Create(m_Graph, muscleConstraintJob);
            var rotationConstraintPlayable = AnimationScriptPlayable.Create(m_Graph, rotationConstraintJob);

            lookAtConstraintPlayable.AddInput(averageConstraintPlayable, 0, 1);
            muscleConstraintPlayable.AddInput(lookAtConstraintPlayable, 0, 1);
            rotationConstraintPlayable.AddInput(muscleConstraintPlayable, 0, 1);
            output.SetSourcePlayable(rotationConstraintPlayable);

            m_Graph.Play();
        }

        void DestroyPlayableGraph()
        {
            if (m_Graph.IsValid())
            {
                averageConstraintJob.Dispose();
                muscleConstraintJob.Dispose();
                lookAtConstraintJob.Dispose();
                rotationConstraintJob.Dispose();
                m_Graph.Destroy();
            }
        }

        static T[] FilterConstraintsByType<T>(Constraint[] constraints)
        {
            List<T> list = new List<T>();
            foreach (Constraint constraint in constraints)
            {
                if (constraint is T)
                    list.Add((T)constraint);
            }

            return list.ToArray();
        }
    }
}
