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
    [Tooltip("How much forward speed affects the length of the stride.")]
    public float speedToStrideDistanceMultiplier = 0.1f;
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

    /// <summary>
    /// The normal of the surface the spider is standing on.
    /// </summary>
    public Vector3 SurfaceNormal { get; private set; }


    private IKSolver mSolver;
    private SimpleController controller; // Reference to the controller
    private GameObject[] mLegTargets;
    private Vector3[] mCurrentPos;     // original position of each leg (before starting a step)
    private Vector3[] mTargetPos;      // target position of each leg (as step is being taken)
    private Vector3[] mCurrentNormals; // The last direction of the leg relative to the body
    private float[] mStepProgress;     // Use a dedicated progress tracker for each leg
    private bool[] mStepping;          // true if a limb is already mid-step
    private int[] mGaitGroup;          // To group legs for alternating gait
    private int mNumLimbs;
    private bool mIsInitialized = false;
    private Vector3[] mIdealLegPositions; // Store the local positions of leg targets (in root local space)

    // which gait group (0 or 1) is allowed to start the next step
    private int mNextGaitGroup = 0;

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

            // parent under ROOT and store ideal pos in ROOT LOCAL SPACE, god this gives me headaches 
            target.transform.SetParent(root);
            target.transform.position = mSolver.limbs[i].target.position;
            mIdealLegPositions[i] = root.InverseTransformPoint(target.transform.position);
            target.transform.rotation = mSolver.limbs[i].target.rotation;

            mLegTargets[i] = target;

            // Tripod group: 0,2,4 vs 1,3,5
            mGaitGroup[i] = i % 2;

            // Initialize current position based on the actual end effector position
            mCurrentPos[i] = mSolver.limbs[i].endEffector.position;
            mCurrentNormals[i] = (mCurrentPos[i] - root.position).normalized;
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
            // Ideal leg point in world space based on root-local offset
            Vector3 idealPosition = root.TransformPoint(mIdealLegPositions[i]);
            RaycastHit hit;
            // The ray should start above the ideal position and cast along the body's "down" direction
            Vector3 rayOrigin = idealPosition + transform.up * raycastOriginOffset;
            Vector3 rayDir = -transform.up;

            if (Physics.Raycast(rayOrigin, rayDir, out hit, raycastDistance, groundLayer))
            {
                mLegTargets[i].transform.position = hit.point;
                Debug.DrawLine(rayOrigin, hit.point, Color.green);
                averageFootNormal += hit.normal;
                groundedLimbs++;
            }
            else
            {
                // If no ground is hit, project the point along the ray direction
                mLegTargets[i].transform.position = rayOrigin + rayDir * (raycastDistance - raycastOriginOffset);
                Debug.DrawLine(rayOrigin, rayOrigin + rayDir * raycastDistance, Color.red);
            }

            // Use actual foot positions for averaging height
            averageFootPosition += mCurrentPos[i];
        }

        averageFootPosition /= mNumLimbs;
        if (groundedLimbs > 0)
        {
            averageFootNormal /= groundedLimbs;
        }
        else
        {
            averageFootNormal = transform.up; // If no limbs are grounded, maintain current orientation
        }

        SurfaceNormal = averageFootNormal;

        // Adapt body position and rotation
        // The final position is a combination of the desired velocity and the adaptation to the ground.
        Vector3 velocity = controller.Velocity;
        Vector3 targetBodyPosition = transform.position + velocity * Time.deltaTime;

        // Project the target position onto the plane of the feet to keep it grounded
        Vector3 positionOnPlane = Vector3.ProjectOnPlane(targetBodyPosition - averageFootPosition, SurfaceNormal) + averageFootPosition;

        // smoothly move the entire spider towards this combined target position
        transform.position = Vector3.Lerp(
            transform.position,
            positionOnPlane,
            Time.deltaTime * bodyAdaptationSpeed
        );

        // rotate the entire spider to align with the surface normal
        Quaternion targetBodyRotation = Quaternion.FromToRotation(transform.up, SurfaceNormal) * transform.rotation;
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetBodyRotation,
            Time.deltaTime * bodyAdaptationSpeed
        );

        // --- Stepping Logic ---
        bool isBodyMoving = controller.HasInput;

        // A new step can start if the other group of legs has finished stepping.
        bool canStartNewStep = !IsGaitGroupStepping(1 - mNextGaitGroup);
        bool groupStartedStepping = false;

        for (int i = 0; i < mNumLimbs; i++)
        {
            // Only allow the designated gait group to start a new step.
            bool canThisGroupStep = canStartNewStep && (mGaitGroup[i] == mNextGaitGroup);

            //Can't believe I forgot make this a 3D distance check! and take an hour!! to debug it
            // Calculate the full 3D distance between the current foot position and its ideal target.
            float distance = Vector3.Distance(mCurrentPos[i], mLegTargets[i].transform.position);

            // --- Start a new step ---
            if (isBodyMoving &&
                (distance > threshold) &&
                !mStepping[i] &&
                canThisGroupStep)
            {
                mStepping[i] = true;
                groupStartedStepping = true;

                // Get the base target position
                Vector3 targetPosition = mLegTargets[i].transform.position;

                // Add an offset based on the spider's forward speed
                Vector3 forwardDirection = Vector3.ProjectOnPlane(transform.forward, SurfaceNormal).normalized;
                float forwardOffset = controller.ForwardSpeed * speedToStrideDistanceMultiplier;
                targetPosition += forwardDirection * forwardOffset;

                mTargetPos[i] = targetPosition;
            }

            // --- Continue / finish the step ---
            if (mStepping[i])
            {
                float linearSpeedBonus = controller.CurrentLinearSpeed * speedToStrideSpeedMultiplier;
                float angularSpeedBonus = controller.CurrentAngularSpeed * angularSpeedToStrideSpeedMultiplier;
                float currentStrideSpeed = Mathf.Clamp(
                    strideSpeed + linearSpeedBonus + angularSpeedBonus,
                    strideSpeed,
                    maxStrideSpeed
                );

                mStepProgress[i] += currentStrideSpeed * Time.deltaTime;
                mStepProgress[i] = Mathf.Clamp01(mStepProgress[i]);

                Vector3 newPos = Vector3.Lerp(mCurrentPos[i], mTargetPos[i], mStepProgress[i]);
                // The "up" for the stride arch should be relative to the body's orientation
                newPos += transform.up * Mathf.Sin(mStepProgress[i] * Mathf.PI) * strideHeight;
                mSolver.limbs[i].target.position = newPos;

                if (mStepProgress[i] >= 1.0f)
                {
                    // The new stable point is the actual position of the foot
                    mCurrentPos[i] = mSolver.limbs[i].endEffector.position;
                    mCurrentNormals[i] = (mCurrentPos[i] - root.position).normalized;

                    mStepProgress[i] = 0f;
                    mStepping[i] = false;
                }
            }
            else
            {
                // Not stepping: keep IK target at planted position
                mSolver.limbs[i].target.position = mCurrentPos[i];
            }
        }

        // If any leg in the current group started stepping, switch to the other group for the next turn.
        if (groupStartedStepping)
        {
            mNextGaitGroup = 1 - mNextGaitGroup;
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