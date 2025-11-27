using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEngine.Rendering;

[System.Serializable]
public struct IKLimbs
{
    [Header("Bones")]
    [Tooltip("Root bone of chain (e.g. shoulder) and end effector (e.g. hand).")]
    public Transform rootBone;
    public Transform endEffector;

    [Header("Targets")]
    [Tooltip("Target that end effector should reach.")]
    public Transform target; // target's world-space transform
    [Tooltip("Pole is optional but recommended for knee/elbow joints.")]
    public Transform pole; // world-space knee/elbow direction helper (optional)

    [HideInInspector] public Transform[] boneTransforms; // length must be >= 2
    [HideInInspector] public Transform[] mBindPoses;
    [HideInInspector] public Vector3[] mPositions; // copy of bone positions
    [HideInInspector] public Quaternion[] mBindPoseLocalRots; // bind pose local rotations
    [HideInInspector] public Vector3[] mBindPoseLocalDirs; // bind pose directions from each bone to their child in local space
    [HideInInspector] public int mEndIndex; // index of end effector
    [HideInInspector] public float[] mBoneLengths; // length of each joint (in other words, distances between each joint to its next joint)
    [HideInInspector] public float mTotalLength; // total length of IK chain
}

public class IKSolver : MonoBehaviour
{
    public IKLimbs[] limbs;

    [Header("Settings")]
    [Tooltip("Increasing iterations makes the IK more precise, but also increases processing required.")]
    [SerializeField] int iterations = 10;
    [Tooltip("Smaller tolerance means the limb will follow the target more closely.")]
    [SerializeField] float tolerance = 0.5f;
    [Tooltip("Turn on debug drawing of IK chain.")]
    [SerializeField] bool debug_draw = true;

    [HideInInspector] public int mNumLimbs; // number of limbs (can be passed into LegStepper)
    private Vector3[] mInitialPoleOffsets; // Store the initial offset of the pole from the end effector
    private bool mPolesInitialized = false;

    void Awake()
    {
        mNumLimbs = limbs.Length;
        if (mNumLimbs == 0)
        {
            Debug.LogError("IK Error: No IK limbs assigned.");
            return;
        }

        for (int b = 0; b < mNumLimbs; b++)
        {
            if (limbs[b].rootBone == null || limbs[b].endEffector == null)
            {
                Debug.LogError("IK Error: No root/end effector assigned for limb " + b + ".");
                return;
            }
            List<Transform> chain = new List<Transform>();
            Transform thisBone = limbs[b].endEffector;
            while(thisBone != null)
            {
                chain.Add(thisBone);
                if (thisBone == limbs[b].rootBone)
                {
                    break;
                }
                thisBone = thisBone.parent;
            }
            chain.Reverse();

            limbs[b].boneTransforms = chain.ToArray();
            Transform[] bones = limbs[b].boneTransforms;
            int numBones = bones.Length;

            // Ensure at least 2 bones assigned for IK to work
            if (numBones < 2)
            {
                Debug.LogError("IK Error: IK chain index " + b + " needs at least 2 bones in chain.");
                return;
            }

            limbs[b].mEndIndex = numBones - 1;
            limbs[b].mBindPoses = new Transform[numBones];
            limbs[b].mPositions = new Vector3[numBones];
            limbs[b].mBoneLengths = new float[numBones];
            limbs[b].mBindPoseLocalRots = new Quaternion[numBones];
            limbs[b].mBindPoseLocalDirs = new Vector3[numBones];

            // If no target set, create one at endEffector's transform
            if (limbs[b].target == null)
            {
                GameObject autoTarget = new GameObject(bones[limbs[b].mEndIndex].name + "_IKTarget");
                autoTarget.transform.position = limbs[b].endEffector.position;
                autoTarget.transform.rotation = limbs[b].endEffector.rotation;
                Debug.Log("IK Warning: No target assigned for limb " + b + ". Created auto-target at end effector position.");
                autoTarget.transform.SetParent(this.transform); // parent target under solver
                limbs[b].target = autoTarget.transform;
            }
            // If no pole set, create one for each middle joint
            if (limbs[b].pole == null && bones.Length >= 3)
            {
                GameObject autoPole = new GameObject(bones[1].name + "_IKPole");

               
                Vector3 rootPos = bones[0].position;
                Vector3 midPos = bones[1].position;
                Vector3 endPos = bones[numBones - 1].position;

                // Vector  root to end point
                Vector3 rootToEnd = endPos - rootPos;
                // Vector root to the middle joint
                Vector3 rootToMid = midPos - rootPos;

                // Find the point on the root-to-end line closest to the middle joint
                Vector3 projectedMid = rootPos + Vector3.Project(rootToMid, rootToEnd);

                // The direction from the projected point to the actual middle joint gives us the bend direction
                Vector3 bendDir = (midPos - projectedMid).normalized;

                // If the limb is perfectly straight, then the direction is zero. set default
                if (bendDir.sqrMagnitude == 0)
                {
                    // Default to the root bone's forward direction and transformed to world space
                    bendDir = bones[0].TransformDirection(Vector3.forward);
                }
                float limbLength = 0;
                for(int i = 0; i < numBones - 1; i++)
                {
                    limbLength += (bones[i+1].position - bones[i].position).magnitude;
                }

                
                float poleDist = limbLength * 0.5f;
                autoPole.transform.position = midPos + bendDir * poleDist;

                autoPole.transform.SetParent(this.transform);
                limbs[b].pole = autoPole.transform;
            }

            for (int i = 0; i < numBones; i++)
            {
                limbs[b].mBindPoses[i] = bones[i];
                limbs[b].mBindPoseLocalRots[i] = bones[i].localRotation;

                if (i < limbs[b].mEndIndex)
                {
                    float length = (bones[i + 1].position - bones[i].position).magnitude;
                    limbs[b].mBoneLengths[i] = length;
                    limbs[b].mTotalLength += length;

                    limbs[b].mBindPoseLocalDirs[i] = bones[i + 1].localPosition.normalized;
                }
                else
                {
                    limbs[b].mBindPoseLocalDirs[i] = Vector3.zero;
                }
            }
        }
    }

    void LateUpdate()
    {
        // This check ensures we only calculate offsets after LegStepper has created its targets
        if (!mPolesInitialized)
        {
            InitializePoleOffsets();
        }

        // Dynamically update pole positions
        if (mPolesInitialized)
        {
            LegStepper stepper = GetComponent<LegStepper>();
            if (stepper != null)
            {
                for (int i = 0; i < mNumLimbs; i++)
                {
                    if (limbs[i].pole != null && stepper.GetLegTarget(i) != null)
                    {
                        // Move the pole to maintain its initial offset from the moving leg target
                        limbs[i].pole.position = stepper.GetLegTarget(i).transform.position + mInitialPoleOffsets[i];
                    }
                }
            }
        }

        for (int i = 0; i < limbs.Length; i++)
        {
            if (limbs[i].target != null)
            {
                DoIK(i);
            }
        }
    }

    private void InitializePoleOffsets()
    {
        LegStepper stepper = GetComponent<LegStepper>();
        // Ensure LegStepper has finished its Start() method and created the targets
        if (stepper != null && stepper.IsInitialized())
        {
            mInitialPoleOffsets = new Vector3[mNumLimbs];
            for (int i = 0; i < mNumLimbs; i++)
            {
                if (limbs[i].pole != null)
                {
                    // Calculate and store the initial world-space offset 
                    // between the pole and the leg's resting target position.
                    mInitialPoleOffsets[i] = limbs[i].pole.position - stepper.GetLegTarget(i).transform.position;
                }
            }
            mPolesInitialized = true;
        }
    }

    // Main IK Solver function using FABRIK algorithm
    void DoIK(int idx)
    {
        Transform[] boneTransforms = limbs[idx].boneTransforms;
        Transform target = limbs[idx].target;
        int endIndex = limbs[idx].mEndIndex;

        Vector3 distance = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 direction = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 rootOriginalPos = boneTransforms[0].position; // Save root's original position

        // Fill mPositions with current bone transform positions
        for (int i = 0; i < boneTransforms.Length; i++)
        {
            limbs[idx].mPositions[i] = boneTransforms[i].position;

            // If setting is on, draw debug line
            if (debug_draw && i < boneTransforms.Length - 1)
            {
                Vector3 d = (limbs[idx].mPositions[i + 1] - limbs[idx].mPositions[i]).normalized;
                Debug.DrawLine(limbs[idx].mPositions[i], limbs[idx].mPositions[i + 1], Color.yellow);
                if (i != 0 && i != boneTransforms.Length - 1)
                {
                    Debug.DrawLine(limbs[idx].pole.position, limbs[idx].mPositions[i], Color.green);
                }
            }
        }

        // If target is out of reach, just stretch towards it
        if (Vector3.Distance(limbs[idx].mPositions[0], target.position) > limbs[idx].mTotalLength)
        {
            // Debug.Log("Out of reach");
            direction = (target.position - limbs[idx].mPositions[0]).normalized; // determine direction towards target
            limbs[idx].mPositions[0] = rootOriginalPos; // ensure root is in its original position
            for (int i = 1; i < boneTransforms.Length; i++)
            {
                limbs[idx].mPositions[i] = limbs[idx].mPositions[i - 1] + direction *  limbs[idx].mBoneLengths[i - 1];
            }
        }
        // Else, target is not out of reach. Apply FABRIK.
        else
        {
            // Debug.Log("FABRIK");
            // ----- START of FABRIK Algorithm -----
            for (int iter = 0; iter < iterations; iter++)
            {
                // 1. Put end effector onto target
                limbs[idx].mPositions[endIndex] = target.position;

                // 2. Backward pass: For each next joint (going UP the chain), move to correct dist and dir to reach end effector
                for (int i = endIndex - 1; i >= 0; i--)
                {
                    // Determine direction
                    direction = (limbs[idx].mPositions[i] - limbs[idx].mPositions[i + 1]).normalized;
                    // Translate current joint to reach next joint
                    limbs[idx].mPositions[i] = limbs[idx].mPositions[i + 1] + direction * limbs[idx].mBoneLengths[i];
                }

                // 3. Move root back to original position
                limbs[idx].mPositions[0] = rootOriginalPos;

                // 4. Forward pass: For each joint (going DOWN the chain), move to correct dist and dir to reach root
                for (int i = 1; i <= endIndex; i++)
                {
                    // Calculate distance between current joint and previous joint in chain
                    direction = (limbs[idx].mPositions[i] - limbs[idx].mPositions[i - 1]).normalized;
                    // Translate current joint to reach previous joint
                    limbs[idx].mPositions[i] = limbs[idx].mPositions[i - 1] + direction * limbs[idx].mBoneLengths[i - 1];
                }
                // If close enough to target, exit loop
                if (Vector3.Distance(limbs[idx].mPositions[endIndex], target.position) < tolerance)
                {
                    break;
                }
            }
            // ----- END of FABRIK Algorithm -----
        }

        // Adjust middle joint(s) to aim towards pole vector
        if (boneTransforms.Length >= 3)
        {
            for (int i = 1; i < endIndex; i++)
            {
                Vector3 rootPos = limbs[idx].mPositions[i - 1];
                Vector3 jointPos = limbs[idx].mPositions[i];
                Vector3 childPos = limbs[idx].mPositions[i + 1];

                // Axis of limb
                Vector3 hingeDir = (childPos - rootPos).normalized;

                // Pole projected onto hinge plane
                Vector3 projectedPole =
                    Vector3.ProjectOnPlane(limbs[idx].pole.position - rootPos, hingeDir).normalized;

                // Joint projected onto same plane
                Vector3 projectedJoint =
                    Vector3.ProjectOnPlane(jointPos - rootPos, hingeDir).normalized;

                float angle = Vector3.SignedAngle(projectedJoint, projectedPole, hingeDir);

                Quaternion rot = Quaternion.AngleAxis(angle, hingeDir);

                // Apply rotation to joint
                limbs[idx].mPositions[i] = rootPos + rot * (jointPos - rootPos);
            }
        }

        // Apply final transforms to bone transforms
        boneTransforms[0].position = limbs[idx].mPositions[0];

        // For each bone, rotate so that it points to its child's pos
        for (int i = 0; i < boneTransforms.Length; i++)
        {
            if (i == endIndex)
            {
                break; // end effector has no child to aim at
            } 

            Vector3 desiredWorldDir = (limbs[idx].mPositions[i + 1] - limbs[idx].mPositions[i]).normalized; // world space direction to child

            Vector3 desiredLocalDir = desiredWorldDir; 
            if (boneTransforms[i].parent != null)
            {
                // convert direction into bone's local space
                desiredLocalDir = boneTransforms[i].parent.InverseTransformDirection(desiredWorldDir);
            }

            if (limbs[idx].mBindPoseLocalDirs[i].sqrMagnitude < float.MinValue)
            {
                Debug.Log("IK Error: Bind pose rotation for bone index " + i + " is invalid.");
                continue;
            }

            Quaternion rot = Quaternion.FromToRotation(limbs[idx].mBindPoseLocalDirs[i], desiredLocalDir);
            boneTransforms[i].localRotation = rot * limbs[idx].mBindPoseLocalRots[i]; // apply rotation towards child onto bind pose rotation
        }
    }
}
