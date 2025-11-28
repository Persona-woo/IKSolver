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

    [Header("Ground Adaptation")]
    [Tooltip("The layer mask to consider as ground.")]
    public LayerMask groundLayer;
    [Tooltip("How far above the root to start the ground-detecting raycast for the feet.")]
    public float raycastOriginOffset = 1.0f;
    [Tooltip("How far down the ray should check for ground.")]
    public float raycastDistance = 2.0f;
    [Tooltip("How quickly the body adapts its height and rotation to the ground.")]
    public float bodyAdaptationSpeed = 5.0f;


    private IKSolver mSolver;
    private SimpleController controller; // Reference to the controller
    private GameObject[] mLegTargets;
    private Vector3[] mCurrentPos; // original position of each leg (before starting a step)
    private Vector3[] mTargetPos; // target position of each leg (as step is being taken)
    private Vector3[] mCurrentNormals; // The last direction of the leg relative to the body
    private float[] mStepProgress; // Use a dedicated progress tracker for each leg
    private bool[] mStepping; // array of bools - true if a limb is already mid-step
    private int[] mGaitGroup; // To group legs for alternating gait
    private int mNumLimbs;
    private bool mIsInitialized = false;
    private Vector3[] mIdealLegPositions; // Store the local positions of leg targets

    void Start()
    {
        mSolver = GetComponent<IKSolver>();
        controller = GetComponent<SimpleController>(); // Get the controller component

        if (mSolver == null || controller == null)
        {
            Debug.LogError("IK Solver or SimpleController not found on this GameObject.");
            return;
        }

        mNumLimbs = mSolver.mNumLimbs;
        mLegTargets = new GameObject[mNumLimbs];
        mCurrentPos = new Vector3[mNumLimbs];
        mTargetPos = new Vector3[mNumLimbs];
        mCurrentNormals = new Vector3[mNumLimbs];
        mStepProgress = new float[mNumLimbs];
        mStepping = new bool[mNumLimbs];
        mGaitGroup = new int[mNumLimbs];
        mIdealLegPositions = new Vector3[mNumLimbs];

        for (int i = 0; i < mNumLimbs; i++)
        {
            GameObject target = new GameObject(mSolver.limbs[i].endEffector.name + "_LegStepperTarget");
            target.transform.SetParent(this.transform);
            target.transform.localPosition = this.transform.InverseTransformPoint(mSolver.limbs[i].target.position);
            mIdealLegPositions[i] = target.transform.localPosition;
            target.transform.rotation = mSolver.limbs[i].target.rotation;
            mLegTargets[i] = target;
            mGaitGroup[i] = i % 2;

            mSolver.limbs[i].target.position += new Vector3(Random.Range(-threshold * 0.1f, threshold * 0.1f), 0, Random.Range(-threshold * 0.1f, threshold * 0.1f));
            mCurrentPos[i] = mSolver.limbs[i].target.position;
            mCurrentNormals[i] = (mLegTargets[i].transform.position - root.position).normalized;
            mStepping[i] = false;
            mStepProgress[i] = 0f;
        }
        mIsInitialized = true;
    }

    void Update()
    {
        // --- Ground Projection and Body Adaptation ---
        Vector3 averageFootPosition = Vector3.zero;
        Vector3 averageFootNormal = Vector3.zero;
        int groundedLimbs = 0;

        for (int i = 0; i < mNumLimbs; i++)
        {
            Vector3 idealPosition = root.TransformPoint(mIdealLegPositions[i]);
            RaycastHit hit;
            Vector3 rayOrigin = idealPosition + root.up * raycastOriginOffset;
            if (Physics.Raycast(rayOrigin, -root.up, out hit, raycastDistance, groundLayer))
            {
                mLegTargets[i].transform.position = hit.point;
                Debug.DrawLine(rayOrigin, hit.point, Color.green);
                averageFootNormal += hit.normal;
                groundedLimbs++;
            }
            else
            {
                mLegTargets[i].transform.position = idealPosition;
                Debug.DrawLine(rayOrigin, rayOrigin - root.up * raycastDistance, Color.red);
            }
            averageFootPosition += mCurrentPos[i];
        }
        averageFootPosition /= mNumLimbs;
        if (groundedLimbs > 0)
        {
            averageFootNormal /= groundedLimbs;
        }
        else
        {
            averageFootNormal = Vector3.up;
        }

        // Adapt body position and rotation
        Vector3 targetBodyPosition = new Vector3(root.position.x, averageFootPosition.y, root.position.z);
        root.position = Vector3.Lerp(root.position, targetBodyPosition, Time.deltaTime * bodyAdaptationSpeed);
        Quaternion targetBodyRotation = Quaternion.FromToRotation(root.up, averageFootNormal) * root.rotation;
        root.rotation = Quaternion.Slerp(root.rotation, targetBodyRotation, Time.deltaTime * bodyAdaptationSpeed);

        // --- Stepping Logic ---
        // Use the controller's input state to determine if the body is "supposed" to be moving.
        bool isBodyMoving = controller.HasInput;

        for (int i = 0; i < mNumLimbs; i++)
        {
            bool canThisGroupStep = (mGaitGroup[i] == 0 && !IsGaitGroupStepping(1)) || (mGaitGroup[i] == 1 && !IsGaitGroupStepping(0));
            float distance = Vector3.Distance(mLegTargets[i].transform.position, mCurrentPos[i]);
            float angle = Vector3.Angle(mCurrentNormals[i], (mLegTargets[i].transform.position - root.position).normalized);

            if (isBodyMoving && (distance > threshold || angle > angleThreshold) && !mStepping[i] && canThisGroupStep)
            {
                mStepping[i] = true;
                mTargetPos[i] = mLegTargets[i].transform.position + (mLegTargets[i].transform.position - mCurrentPos[i]).normalized * threshold * 0.5f;
            }

            if (mStepping[i])
            {
                // Use the intended speed from the controller for dynamic stride speed calculation
                float linearSpeedBonus = controller.CurrentLinearSpeed * speedToStrideSpeedMultiplier;
                float angularSpeedBonus = controller.CurrentAngularSpeed * angularSpeedToStrideSpeedMultiplier;
                float currentStrideSpeed = Mathf.Clamp(strideSpeed + linearSpeedBonus + angularSpeedBonus, strideSpeed, maxStrideSpeed);

                mStepProgress[i] += currentStrideSpeed * Time.deltaTime;
                mStepProgress[i] = Mathf.Clamp01(mStepProgress[i]);
                Vector3 newPos = Vector3.Lerp(mCurrentPos[i], mTargetPos[i], mStepProgress[i]);
                newPos.y += Mathf.Sin(mStepProgress[i] * Mathf.PI) * strideHeight;
                mSolver.limbs[i].target.position = newPos;

                if (mStepProgress[i] >= 1.0f)
                {
                    mSolver.limbs[i].target.position = mTargetPos[i];
                    mCurrentPos[i] = mTargetPos[i];
                    mCurrentNormals[i] = (mLegTargets[i].transform.position - root.position).normalized;
                    mStepProgress[i] = 0f;
                    mStepping[i] = false;
                }
            }
            else
            {
                mSolver.limbs[i].target.position = mCurrentPos[i];
            }
        }
    }

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