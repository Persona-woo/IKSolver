using Unity.VisualScripting;
using UnityEngine;

public class ManLegStepper : MonoBehaviour
{
    [Header("Stepping Properties")]
    [Tooltip("Center of gravity to base leg positions on (usually hips/pelvis).")]
    public Transform root;
    [Tooltip("Distance away from ideal position before leg should take a step.")]
    public float threshold = 0.4f;
    [Tooltip("The angle (in degrees) the ideal leg position can rotate before a step is triggered.")]
    public float angleThreshold = 15f;
    [Tooltip("The base speed of the step.")]
    public float strideSpeed = 5f;
    [Tooltip("How high each leg raises with each step.")]
    public float strideHeight = 0.3f;

    [Header("Humanoid Leg Spacing")]
    [Tooltip("How far apart the feet are (left to right).")]
    public float stanceWidth = 0.3f;
    [Tooltip("How far forward from root the feet plant.")]
    public float footForwardOffset = 0.2f;

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

    /// <summary>
    /// The normal of the surface the character is standing on.
    /// </summary>
    public Vector3 SurfaceNormal { get; private set; }

    private IKSolver mSolver;
    private ManSimpleController controller; // Reference to the controller
    private GameObject[] mLegTargets;
    private Vector3[] mCurrentPos;     // original position of each leg (before starting a step)
    private Vector3[] mTargetPos;      // target position of each leg (as step is being taken)
    private Vector3[] mCurrentNormals; // The last direction of the leg relative to the body
    private float[] mStepProgress;     // Use a dedicated progress tracker for each leg
    private bool[] mStepping;          // true if a limb is already mid-step
    private int mNumLimbs = 2;         // Humanoid has 2 legs
    private bool mIsInitialized = false;
    private Vector3[] mIdealLegPositions; // Store the local positions of leg targets (in root local space)

    // For alternating gait: 0 = left leg can step, 1 = right leg can step
    private int mNextLegToStep = 0;

    // Track body movement
    private Vector3 mLastRootPosition;
    private Quaternion mLastRootRotation;

    void Start()
    {
        mSolver = GetComponent<IKSolver>();
        controller = GetComponent<ManSimpleController>(); // Get the controller component

        if (mSolver == null)
        {
            Debug.LogError("IK Solver not found on this GameObject.");
            return;
        }

        if (controller == null)
        {
            Debug.LogWarning("SimpleController not found. LegStepper will work but without velocity-based adaptation.");
        }

        if (mSolver.mNumLimbs != 2)
        {
            Debug.LogError("LegStepper Error: IKSolver should have exactly 2 limbs for humanoid (left leg index 0, right leg index 1)!");
            return;
        }

        mNumLimbs = 2; // Force 2 legs for humanoid
        mLegTargets = new GameObject[mNumLimbs];
        mCurrentPos = new Vector3[mNumLimbs];
        mTargetPos = new Vector3[mNumLimbs];
        mCurrentNormals = new Vector3[mNumLimbs];
        mStepProgress = new float[mNumLimbs];
        mStepping = new bool[mNumLimbs];
        mIdealLegPositions = new Vector3[mNumLimbs];

        for (int i = 0; i < mNumLimbs; i++)
        {
            GameObject target = new GameObject(mSolver.limbs[i].endEffector.name + "_LegStepperTarget");

            // Parent under ROOT and store ideal pos in ROOT LOCAL SPACE
            target.transform.SetParent(root);

            // Calculate ideal foot positions based on stance width
            // i = 0 is left leg (negative X), i = 1 is right leg (positive X)
            float lateralOffset = (i == 0) ? -stanceWidth : stanceWidth;
            Vector3 localIdealPos = new Vector3(lateralOffset, 0f, footForwardOffset);

            // If IK solver already has targets, use their positions as starting point
            if (mSolver.limbs[i].target != null)
            {
                target.transform.position = mSolver.limbs[i].target.position;
                mIdealLegPositions[i] = root.InverseTransformPoint(target.transform.position);
            }
            else
            {
                // Otherwise use calculated ideal position
                mIdealLegPositions[i] = localIdealPos;
                target.transform.position = root.TransformPoint(localIdealPos);
            }

            target.transform.rotation = root.rotation;

            mLegTargets[i] = target;

            // Initialize current position based on the actual end effector position
            mCurrentPos[i] = mSolver.limbs[i].endEffector.position;
            mCurrentNormals[i] = (mCurrentPos[i] - root.position).normalized;
            mStepping[i] = false;
            mStepProgress[i] = 0f;
        }

        // Initialize tracking for body movement
        mLastRootPosition = root.position;
        mLastRootRotation = root.rotation;

        mIsInitialized = true;
        Debug.Log("LegStepper: Initialized for humanoid with 2 legs");
    }

    void Update()
    {
        if (!mIsInitialized)
            return;

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

                // Use the target positions (where feet SHOULD be) for height calculation
                averageFootPosition += hit.point;
                groundedLimbs++;
            }
            else
            {
                // If no ground is hit, project the point along the ray direction
                mLegTargets[i].transform.position = rayOrigin + rayDir * (raycastDistance - raycastOriginOffset);
                Debug.DrawLine(rayOrigin, rayOrigin + rayDir * raycastDistance, Color.red);

                // Still include this position in average
                averageFootPosition += mLegTargets[i].transform.position;
            }
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

        // Adapt body position and rotation (only if controller exists)
        if (controller != null)
        {
            Vector3 velocity = controller.Velocity;
            Debug.Log(velocity);
            Vector3 targetBodyPosition = transform.position + velocity * Time.deltaTime;

            // Calculate desired height based on average foot position
            // The root should be at a fixed offset above the average foot position
            float desiredHeight = averageFootPosition.y + Vector3.Distance(root.position, averageFootPosition);
            Vector3 horizontalPosition = new Vector3(targetBodyPosition.x, transform.position.y, targetBodyPosition.z);

            // Smoothly adjust height to match ground level
            Vector3 finalPosition = new Vector3(
                horizontalPosition.x,
                Mathf.Lerp(transform.position.y, desiredHeight, Time.deltaTime * bodyAdaptationSpeed),
                horizontalPosition.z
            );

            transform.position = finalPosition;

            // Rotate the character to align with the surface normal (less aggressive for humanoids)
            Quaternion targetBodyRotation = Quaternion.FromToRotation(transform.up, SurfaceNormal) * transform.rotation;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetBodyRotation,
                Time.deltaTime * bodyAdaptationSpeed * 0.5f // Half speed for rotation to keep humanoid more upright
            );
        }

        // --- Stepping Logic ---
        // Check if body is actually moving (fallback if no controller or controller not working)
        float bodyMovementSpeed = Vector3.Distance(root.position, mLastRootPosition) / Time.deltaTime;
        float bodyRotationSpeed = Quaternion.Angle(root.rotation, mLastRootRotation) / Time.deltaTime;
        bool bodyIsMoving = bodyMovementSpeed > 0.01f || bodyRotationSpeed > 0.5f;

        // Use controller input if available, otherwise use actual body movement
        bool isBodyMoving = controller != null ? (controller.HasInput || bodyIsMoving) : bodyIsMoving;

        // Update tracking
        mLastRootPosition = root.position;
        mLastRootRotation = root.rotation;

        // Humanoid alternating gait: only one leg steps at a time
        int oppositeLeg = 1 - mNextLegToStep;
        bool canStartNewStep = !mStepping[oppositeLeg]; // Can only step if opposite leg is planted

        for (int i = 0; i < mNumLimbs; i++)
        {
            // Only the designated leg can start a step
            bool canThisLegStep = canStartNewStep && (i == mNextLegToStep);

            // Calculate the full 3D distance between the current foot position and its ideal target
            float distance = Vector3.Distance(mCurrentPos[i], mLegTargets[i].transform.position);

            // --- Start a new step ---
            if (isBodyMoving &&
                (distance > threshold) &&
                !mStepping[i] &&
                canThisLegStep)
            {
                mStepping[i] = true;

                // Step toward ground target, slightly past it for smoother motion
                mTargetPos[i] = mLegTargets[i].transform.position
                                + (mLegTargets[i].transform.position - mCurrentPos[i]).normalized * 0.05f;

                // Switch to opposite leg for next step
                mNextLegToStep = 1 - mNextLegToStep;
            }

            // --- Continue / finish the step ---
            if (mStepping[i])
            {
                float currentStrideSpeed = strideSpeed;

                if (controller != null)
                {
                    float linearSpeedBonus = controller.CurrentLinearSpeed * speedToStrideSpeedMultiplier;
                    float angularSpeedBonus = controller.CurrentAngularSpeed * angularSpeedToStrideSpeedMultiplier;
                    currentStrideSpeed = Mathf.Clamp(
                        strideSpeed + linearSpeedBonus + angularSpeedBonus,
                        strideSpeed,
                        maxStrideSpeed
                    );
                }

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

    void OnDrawGizmos()
    {
        if (!mIsInitialized)
            return;

        for (int i = 0; i < mNumLimbs; i++)
        {
            if (mLegTargets[i] == null)
                continue;

            // Draw current foot position
            Gizmos.color = mStepping[i] ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(mCurrentPos[i], 0.05f);

            // Draw target position
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(mLegTargets[i].transform.position, 0.05f);

            // Draw line between current and target
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(mCurrentPos[i], mLegTargets[i].transform.position);

            // Draw step threshold
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(mLegTargets[i].transform.position, threshold);
        }

        // Draw root
        if (root != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(root.position, 0.1f);
        }
    }
}