
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ConstraintSetup : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("CONTEXT/ConstraintSetup/Create Constraints")]
    static void DoCreateConstraints(MenuCommand command)
    {
        ConstraintSetup constraintSetup = command.context as ConstraintSetup;

        constraintSetup.CreateConstraints();
    }
#endif

    public abstract class Constraint
    {
        public Constraint(string groupName)
        {
            this.groupName = groupName;
        }

        public bool LogError(string message)
        {
            Debug.LogError(String.Format("{0} constraint {1} - {2}", GetType().Name, groupName, message));
            return false;
        }

        public abstract bool AddMember(Transform transform);

        public abstract bool Validate();

        public abstract bool CreateComponents();

        protected string groupName;
    }

    public class POC : Constraint
    {
        public POC(string groupName) : base(groupName)
        {
        }

        public override bool AddMember(Transform transform)
        {
            if (transform.name.StartsWith("Loc"))
            {
                if (locatorA == null)
                {
                    locatorA = transform;
                    return true;
                }
                else if (locatorB == null)
                {
                    locatorB = transform;
                    return true;
                }
                else
                {
                    return LogError(
                        String.Format("Already has 2 locator nodes, found {0}",
                            transform.gameObject.name));
                }
            }
            else if (transform.name.StartsWith("Bone"))
            {
                if (bone == null)
                {
                    bone = transform;
                    return true;
                }
                else
                {
                    return LogError(
                        String.Format("Already has bone nodes, found {0}",
                            transform.gameObject.name));
                }
            }
            else
            {
                return LogError(
                    String.Format("Unknown prefix for {0}",
                        transform.gameObject.name));
            }
        }

        public override bool Validate()
        {
            if (locatorA == null)
            {
                return LogError("Missing locator A");
            }
            else if (locatorB == null)
            {
                return LogError("Missing locator B");
            }
            else if (bone == null)
            {
                return LogError("Missing bone");
            }

            return true;
        }

        public override bool CreateComponents()
        {
            Unit.AverageConstraint constraint = bone.gameObject.AddComponent<Unit.AverageConstraint>();

            constraint.startTransform = locatorA;
            constraint.endTransform = locatorB;

            return true;
        }

        Transform locatorA;
        Transform locatorB;
        Transform bone;
    }

    public class Piston : Constraint
    {
        public Piston(string groupName) : base(groupName)
        {
        }

        public override bool AddMember(Transform transform)
        {
            if (transform.name.StartsWith("Loc"))
            {
                if (transform.name.Contains("Start"))
                {
                    locatorStart = transform;
                    return true;
                }
                else if (transform.name.Contains("End"))
                {
                    locatorEnd = transform;
                    return true;
                }
                else
                {
                    return LogError(
                        String.Format("Can't process locator node {0}",
                            transform.gameObject.name));
                }
            }
            else if (transform.name.StartsWith("Bone"))
            {
                if (transform.name.Contains("Start"))
                {
                    boneStart = transform;
                    return true;
                }
                else if (transform.name.Contains("End"))
                {
                    boneEnd = transform;
                    return true;
                }
                else
                {
                    return LogError(
                        String.Format("Can't process bone node {0}",
                            transform.gameObject.name));
                }
            }
            else
            {
                return LogError(
                    String.Format("Unknown prefix for {0}",
                        transform.gameObject.name));
            }
        }

        public override bool Validate()
        {
            if (locatorStart == null)
            {
                return LogError("Missing start locator");
            }
            else if (locatorEnd == null)
            {
                return LogError("Missing end locator");
            }
            else if (boneStart == null)
            {
                return LogError("Missing start bone");
            }
            else if (boneEnd == null)
            {
                return LogError("Missing end bone");
            }

            return true;
        }

        public override bool CreateComponents()
        {
            Unit.LookAtConstraint startConstraint = boneStart.gameObject.AddComponent<Unit.LookAtConstraint>();

            startConstraint.targetTransform = boneEnd;
            startConstraint.upAxisTransform = locatorStart;
            startConstraint.lookAtAxis.axis = Unit.LookAtConstraint.Axis.X;
            startConstraint.lookAtAxis.negative = true;
            startConstraint.upAxis.axis = Unit.LookAtConstraint.Axis.Y;
            startConstraint.upAxis.negative = false;

            Unit.LookAtConstraint endConstraint = boneEnd.gameObject.AddComponent<Unit.LookAtConstraint>();

            endConstraint.targetTransform = boneStart;
            endConstraint.upAxisTransform = locatorEnd;
            endConstraint.lookAtAxis.axis = Unit.LookAtConstraint.Axis.X;
            endConstraint.lookAtAxis.negative = true;
            endConstraint.upAxis.axis = Unit.LookAtConstraint.Axis.Y;
            endConstraint.upAxis.negative = false;

            return true;
        }

        protected Transform locatorStart;
        protected Transform locatorEnd;
        protected Transform boneStart;
        protected Transform boneEnd;
    }

    public class Muscle : Piston
    {
        public Muscle(string groupName) : base(groupName)
        {
        }

        public override bool AddMember(Transform transform)
        {
            if (transform.name.StartsWith("Loc"))
            {
                return base.AddMember(transform);
            }
            else if (transform.name.StartsWith("Bone"))
            {
                if (transform.name.Contains("Stretch"))
                {
                    boneStretch = transform;
                    return true;
                }
                else if (transform.name.Contains("Nub"))
                {
                    return true;
                }
                else
                {
                    return base.AddMember(transform);
                }
            }
            else
            {
                return base.AddMember(transform);
            }
        }

        public override bool Validate()
        {
            if (boneStretch == null)
            {
                return LogError("Missing stretch bone");
            }

            return base.Validate();
        }

        public override bool CreateComponents()
        {
            if (base.CreateComponents())
            {
                Unit.MuscleConstraint constraint = boneStretch.gameObject.AddComponent<Unit.MuscleConstraint>();

                float referenceLength =
                    (locatorStart.position -
                        locatorEnd.position).magnitude;

                constraint.startTransform = locatorStart;
                constraint.endTransform = locatorEnd;
                constraint.referenceLength = referenceLength;
                constraint.multiplier = 1.0f;

                return true;
            }

            return false;
        }

        Transform boneStretch;
    }

    public void CreateConstraints()
    {
        var dictionary = new Dictionary<string, Constraint>();

        var transforms = GetComponentsInChildren<Transform>();
        foreach (var transform in transforms)
        {
            var name = transform.gameObject.name;

            if (name.Equals("Bone_Spinatus_EndR"))
            {
                name = "Bone_Piston_Spinatus_EndR";
            }

            Type type = NameToType(name);
            if (type != null)
            {
                string groupName = GroupName(type, name);
                if (!dictionary.ContainsKey(groupName))
                {
                    dictionary[groupName] =
                        Activator.CreateInstance(type, groupName) as Constraint;
                }

                Assert.IsTrue(dictionary.ContainsKey(groupName));
                if (!dictionary[groupName].AddMember(transform))
                {
                    return;
                }
            }
        }

        foreach (var constraint in dictionary)
        {
            if (!constraint.Value.Validate())
            {
                return;
            }
        }

        RemoveAllConstraintComponents();

        foreach (var constraint in dictionary)
        {
            if (!constraint.Value.CreateComponents())
            {
                return;
            }
        }
    }

    public void RemoveAllConstraintComponents()
    {
        RemoveAllConstraints<Unit.LookAtConstraint>();
        RemoveAllConstraints<Unit.MuscleConstraint>();
        RemoveAllConstraints<Unit.AverageConstraint>();
    }

    public void RemoveAllConstraints<T>() where T : UnityEngine.Object
    {
        var constraints = GetComponentsInChildren<T>();
        foreach (var constraint in constraints)
        {
            DestroyImmediate(constraint);
        }

        Assert.IsTrue(GetComponentsInChildren<T>().Length == 0);
    }

    public string GroupName(Type type, string name)
    {
        string[] words = name.Split('_');

        if (words.Length >= 3)
        {
            char side = name[name.Length - 1];
            if (side == 'L' || side == 'R')
            {
                return type.Name + words[2] + side;
            }
            return type.Name + words[2];
        }

        return string.Empty;
    }

    public Type NameToType(string name)
    {
        string[] words = name.Split('_');

        if (words.Length >= 3)
        {
            return TypeNameToType(words[1]);
        }

        return null;
    }

    public Type TypeNameToType(string name)
    {
        if (name.Equals("POC"))
        {
            return typeof(POC);
        }
        else if (name.Equals("Muscle"))
        {
            return typeof(Muscle);
        }
        else if (name.Equals("Piston"))
        {
            return typeof(Piston);
        }

        return null;
    }
}