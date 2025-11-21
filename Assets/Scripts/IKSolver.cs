using Unity.VisualScripting;
using UnityEngine;

public class IKSolver : MonoBehaviour
{
    [Header("Chain")]
    [Tooltip("Bones assigned from root (e.g. thigh) to end effector (e.g. foot).")]
    public Transform[] boneTransforms; // length must be >= 2

    [Header("Targets")]
    [Tooltip("Target that end effector should reach.")]
    public Transform target; // target's world-space transform
    [Tooltip("Pole is optional but recommended for knee/elbow joints.")]
    public Transform pole; // world-space knee/elbow direction helper (optional)

    [Header("Settings")]
    [Tooltip("Increasing iterations makes the IK more precise, but also increases processing required.")]
    [SerializeField] int iterations = 10;
    [Tooltip("Smaller tolerance means the limb will follow the target more closely.")]
    [SerializeField] float tolerance = 0.5f;
    [Tooltip("Turn on debug drawing of IK chain.")]
    [SerializeField] bool debug_draw = true;

    private Transform[] mBindPoses;
    private Vector3[] mPositions; // copy of bone positions
    private Quaternion[] mBindPoseLocalRots; // bind pose local rotations
    private Vector3[] mBindPoseLocalDirs; // bind pose directions from each bone to their child in local space
    private int mEndIndex; // index of end effector
    private float[] mBoneLengths; // length of each joint (in other words, distances between each joint to its next joint)
    private float mTotalLength = 0.0f; // total length of IK chain

    void Awake()
    {
        int count = boneTransforms.Length;
        // Ensure at least 2 bones assigned for IK to work
        if (boneTransforms == null || count < 2)
        {
            Debug.LogError("IK Error: Needs at least 2 bones in chain.");
            return;
        }
        if (target == null)
        {
            Debug.LogError("IK Error: No target set for IK chain.");
            return;
        }

        // Allocate memory + Save end index, bone positions, rotations and length of each joint
        mEndIndex = count - 1;
        mBindPoses = new Transform[count];
        mPositions = new Vector3[count];
        mBoneLengths = new float[count];

        mBindPoseLocalRots = new Quaternion[count];
        mBindPoseLocalDirs = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            mBindPoses[i] = boneTransforms[i];

            mBindPoseLocalRots[i] = boneTransforms[i].localRotation;

            if (i != mEndIndex)
            {
                mBoneLengths[i] = (boneTransforms[i + 1].position - boneTransforms[i].position).magnitude;
                mTotalLength += mBoneLengths[i];
                mBindPoseLocalDirs[i] = boneTransforms[i + 1].localPosition.normalized;
            }
            else
            {
                mBindPoseLocalDirs[i] = Vector3.zero;
            }
        }
    }

    void LateUpdate()
    {
        if (enabled && target != null)
        {
            DoIK();
        }
    }

    // Main IK Solver function using FABRIK algorithm
    void DoIK()
    {
        Vector3 distance = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 direction = new Vector3(0.0f, 0.0f, 0.0f);
        Vector3 rootOriginalPos = boneTransforms[0].position; // Save root's original position

        // Fill mPositions with current bone transform positions
        for (int i = 0; i < boneTransforms.Length; i++)
        {
            mPositions[i] = boneTransforms[i].position;

            // If setting is on, draw debug line
            if (debug_draw && i < boneTransforms.Length - 1)
            {
                Vector3 d = (mPositions[i + 1] - mPositions[i]).normalized;
                Debug.DrawLine(mPositions[i], mPositions[i + 1], Color.yellow);
                // Debug.DrawLine(mPositions[i], mPositions[i] + d * 0.1f, Color.green);
            }
        }

        // If target is out of reach, just stretch towards it
        if (Vector3.Distance(mPositions[0], target.position) > mTotalLength)
        {
            // Debug.Log("Out of reach");
            direction = (target.position - mPositions[0]).normalized; // determine direction towards target
            mPositions[0] = rootOriginalPos; // ensure root is in its original position
            for (int i = 1; i < boneTransforms.Length; i++)
            {
                mPositions[i] = mPositions[i - 1] + direction * mBoneLengths[i - 1];
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
                mPositions[mEndIndex] = target.position;

                // 2. Backward pass: For each next joint (going UP the chain), move to correct dist and dir to reach end effector
                for (int i = mEndIndex - 1; i >= 0; i--)
                {
                    // Determine direction
                    direction = (mPositions[i] - mPositions[i + 1]).normalized;
                    // Translate current joint to reach next joint
                    mPositions[i] = mPositions[i + 1] + direction * mBoneLengths[i];
                }

                // 3. Move root back to original position
                mPositions[0] = rootOriginalPos;

                // 4. Forward pass: For each joint (going DOWN the chain), move to correct dist and dir to reach root
                for (int i = 1; i <= mEndIndex; i++)
                {
                    // Calculate distance between current joint and previous joint in chain
                    direction = (mPositions[i] - mPositions[i - 1]).normalized;
                    // Translate current joint to reach previous joint
                    mPositions[i] = mPositions[i - 1] + direction * mBoneLengths[i - 1];
                }
                // If close enough to target, exit loop
                if (Vector3.Distance(mPositions[mEndIndex], target.position) < tolerance)
                {
                    break;
                }
            }
            // ----- END of FABRIK Algorithm -----
        }

        // Apply final transforms to bone transforms
        boneTransforms[0].position = mPositions[0];

        // For each bone, rotate so that it points to its child's pos
        for (int i = 0; i < boneTransforms.Length; i++)
        {
            if (i == mEndIndex)
            {
                break; // end effector has no child to aim at
            } 

            Vector3 desiredWorldDir = (mPositions[i + 1] - mPositions[i]).normalized; // world space direction to child

            Vector3 desiredLocalDir = desiredWorldDir; 
            if (boneTransforms[i].parent != null)
            {
                // convert direction into bone's local space
                desiredLocalDir = boneTransforms[i].parent.InverseTransformDirection(desiredWorldDir);
            }

            if (mBindPoseLocalDirs[i].sqrMagnitude < float.MinValue)
            {
                Debug.Log("IK Error: Bind pose rotation for bone index " + i + " is invalid.");
                continue;
            }

            Quaternion rot = Quaternion.FromToRotation(mBindPoseLocalDirs[i], desiredLocalDir);
            boneTransforms[i].localRotation = rot * mBindPoseLocalRots[i]; // apply rotation towards child onto bind pose rotation
        }
    }
}
