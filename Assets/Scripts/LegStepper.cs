using Unity.VisualScripting;
using UnityEngine;

public class LegStepper : MonoBehaviour
{
    [Header("Stepping Properties")]
    [Tooltip("Center of gravity to base leg positions on.")]
    public Transform root;
    [Tooltip("Distance away from root before leg should take a step.")]
    public float threshold;
    [Tooltip("The angle (in degrees) the ideal leg position can rotate before a step is triggered.")]
    public float angleThreshold = 15f;
    [Tooltip("The base speed of the step.")]
    public float strideSpeed = 5f;
    [Tooltip("How high each leg raises with each step.")]
    public float strideHeight;

    [Header("Dynamic Stride Speed")]
    [Tooltip("How much the body's linear speed affects the stride speed.")]
    public float speedToStrideSpeedMultiplier = 1.0f;
    [Tooltip("How much the body's angular speed (deg/s) affects the stride speed.")]
    public float angularSpeedToStrideSpeedMultiplier = 0.1f;
    [Tooltip("The maximum speed a step can have.")]
    public float maxStrideSpeed = 15f;


    private IKSolver mSolver;
    private GameObject[] mLegTargets;
    private Vector3[] mCurrentPos; // original position of each leg (before starting a step)
    private Vector3[] mTargetPos; // target position of each leg (as step is being taken)
    private Vector3[] mCurrentNormals; // The last direction of the leg relative to the body
    private float[] mStepProgress; // Use a dedicated progress tracker for each leg
    private bool[] mStepping; // array of bools - true if a limb is already mid-step
    private int[] mGaitGroup; // To group legs for alternating gait
    private int mNumLimbs;
    private bool mIsInitialized = false;
    private Vector3 lastRootPosition;
    private Quaternion lastRootRotation;
    private float rootSpeed;
    private float rootAngularSpeed;

    void Start()
    {
        mSolver = GetComponent<IKSolver>();
        if (mSolver == null)
        {
            Debug.Log("IK Solver not found. Object must have IK Solver attached to it.");
            return;
        }
        mNumLimbs = mSolver.mNumLimbs;
        mLegTargets = new GameObject[mNumLimbs];
        mCurrentPos = new Vector3[mNumLimbs];
        mTargetPos = new Vector3[mNumLimbs];
        mCurrentNormals = new Vector3[mNumLimbs];
        mStepProgress = new float[mNumLimbs]; // Initialize the progress tracker
        mStepping = new bool[mNumLimbs];
        mGaitGroup = new int[mNumLimbs]; // Initialize gait group array

        for (int i = 0; i < mNumLimbs; i++)
        {
            GameObject target = new GameObject(mSolver.limbs[i].endEffector.name + "_LegStepperTarget");


            target.transform.SetParent(this.transform);


            target.transform.localPosition = this.transform.InverseTransformPoint(mSolver.limbs[i].target.position);
            target.transform.rotation = mSolver.limbs[i].target.rotation;

            mLegTargets[i] = target;

            // Assign to a gait group (e.g., odd/even)
            mGaitGroup[i] = i % 2;

            // Add a small random offset to the initial position to break sync
            mSolver.limbs[i].target.position += new Vector3(Random.Range(-threshold * 0.1f, threshold * 0.1f), 0, Random.Range(-threshold * 0.1f, threshold * 0.1f));

            mCurrentPos[i] = mSolver.limbs[i].target.position;
            mCurrentNormals[i] = (mLegTargets[i].transform.position - root.position).normalized;
            mStepping[i] = false;
            mStepProgress[i] = 0f; // Initial progress is 0
        }
        mIsInitialized = true;
        lastRootPosition = root.position;
        lastRootRotation = root.rotation;
    }

    void Update()
    {
        // Calculate root's linear and angular speed
        Vector3 rootVelocity = (root.position - lastRootPosition) / Time.deltaTime;
        rootSpeed = new Vector2(rootVelocity.x, rootVelocity.z).magnitude;
        lastRootPosition = root.position;

        float angleDifference = Quaternion.Angle(root.rotation, lastRootRotation);
        rootAngularSpeed = angleDifference / Time.deltaTime;
        lastRootRotation = root.rotation;


        for (int i = 0; i < mNumLimbs; i++)
        {
            // Determine if this leg's group is allowed to step.
            bool canThisGroupStep = (mGaitGroup[i] == 0 && !IsGaitGroupStepping(1)) || (mGaitGroup[i] == 1 && !IsGaitGroupStepping(0));

            // Calculate distance and angle difference
            float distance = Vector3.Distance(mLegTargets[i].transform.position, mCurrentPos[i]);
            float angle = Vector3.Angle(mCurrentNormals[i], (mLegTargets[i].transform.position - root.position).normalized);

            // Condition to start a step:
            if ((distance > threshold || angle > angleThreshold) && !mStepping[i] && canThisGroupStep)
            {
                mStepping[i] = true;
                // Target is the ideal resting spot, plus an "overshoot" in the direction of movement to create propulsion
                mTargetPos[i] = mLegTargets[i].transform.position + (mLegTargets[i].transform.position - mCurrentPos[i]).normalized * threshold * 0.5f;
            }

            if (mStepping[i])
            {
                // Calculate the dynamic stride speed for this frame, considering both linear and angular speed
                float linearSpeedBonus = rootSpeed * speedToStrideSpeedMultiplier;
                float angularSpeedBonus = rootAngularSpeed * angularSpeedToStrideSpeedMultiplier;
                float currentStrideSpeed = Mathf.Clamp(strideSpeed + linearSpeedBonus + angularSpeedBonus, strideSpeed, maxStrideSpeed);

                // Update the progress of the step based on the dynamic speed and time
                mStepProgress[i] += currentStrideSpeed * Time.deltaTime;

                // Clamp progress to a 0-1 range
                mStepProgress[i] = Mathf.Clamp01(mStepProgress[i]);

                // Perform the interpolation based on the consistent progress value
                Vector3 newPos = Vector3.Lerp(mCurrentPos[i], mTargetPos[i], mStepProgress[i]);

                // Vertical movement (height) using a sine wave for a smooth arc
                newPos.y += Mathf.Sin(mStepProgress[i] * Mathf.PI) * strideHeight;

                mSolver.limbs[i].target.position = newPos;

                // Check if the step is complete
                if (mStepProgress[i] >= 1.0f)
                {
                    mSolver.limbs[i].target.position = mTargetPos[i];
                    mCurrentPos[i] = mTargetPos[i]; // The new fixed position is the target
                    mCurrentNormals[i] = (mLegTargets[i].transform.position - root.position).normalized; // Update the normal for the new position
                    mStepProgress[i] = 0f; // Reset progress
                    mStepping[i] = false;
                }
            }
            else
            {
                // If not stepping, the foot stays planted at its last position
                mSolver.limbs[i].target.position = mCurrentPos[i];
            }
        }
    }

    // Checks if any leg within a specific gait group is currently stepping
    private bool IsGaitGroupStepping(int group)
    {
        for (int i = 0; i < mNumLimbs; i++)
        {
            if (mGaitGroup[i] == group && mStepping[i])
            {
                return true;
            }
        }
        return false;
    }

    public bool IsInitialized()
    {
        return mIsInitialized;
    }

    public GameObject GetLegTarget(int index)
    {
        if (index >= 0 && index < mNumLimbs)
        {
            return mLegTargets[index];
        }
        return null;
    }
}