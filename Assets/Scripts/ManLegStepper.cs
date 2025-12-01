using Unity.VisualScripting;
using UnityEngine;
using System.Collections;
[RequireComponent(typeof(ManIKSolver))]
public class ManLegStepper : MonoBehaviour
{
    [Header("Stepping Properties")]
    [Tooltip("Center of gravity to base leg positions on.")]
    public Transform root;
    [Tooltip("Distance away from root before leg should take a step.")]
    public float threshold = 1.5f;
    [Tooltip("Duration of a single step in seconds.")]
    public float stepDuration = 0.3f;
    [Tooltip("How high the foot lifts during a step.")]
    public float stepHeight = 0.5f;
    [Tooltip("Animation curve for step interpolation.")]
    public AnimationCurve stepCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private int mNumLimbs;
    private Vector3[] mCurrentTarget;
    private Vector3[] mPreviousTarget;
    private Transform[] mTargetTransforms;
    private bool[] mIsStepping;
    private float[] mStepProgress;
    private int mLastSteppedLeg = -1;

    /// <summary>
    /// The normal of the surface the character is standing on.
    /// </summary>
    public Vector3 SurfaceNormal { get; private set; }
    private ManIKSolver mSolver;
    private ManSimpleController controller;

    void Start()
    {
        mSolver = GetComponent<ManIKSolver>();
        controller = GetComponent<ManSimpleController>();
        if (root == null)
        {
            root = transform;
        }
        // Initialize ideal positions and rotations
        SurfaceNormal = Vector3.up;
        mNumLimbs = mSolver.mNumLimbs;
        mCurrentTarget = new Vector3[mNumLimbs];
        mPreviousTarget = new Vector3[mNumLimbs];
        mTargetTransforms = new Transform[mNumLimbs];
        mIsStepping = new bool[mNumLimbs];
        mStepProgress = new float[mNumLimbs];

        for (int i = 0; i < mNumLimbs; ++i)
        {
            mCurrentTarget[i] = mSolver.limbs[i].target.position;
            mPreviousTarget[i] = mCurrentTarget[i];
            mIsStepping[i] = false;
            mStepProgress[i] = 0f;

            // Create the target GameObject
            GameObject targetObj = new GameObject($"LegTarget_{i}");
            targetObj.transform.SetParent(this.transform);
            targetObj.transform.localPosition = new Vector3((i * 2 - 1) * 0.2f, 0, 2.0f);
            mTargetTransforms[i] = targetObj.transform;
        }
    }

    void Update()
    {
        Vector3 velocity = controller.Velocity;
        Vector3 targetBodyPosition = transform.position + velocity * Time.deltaTime;
        transform.position = targetBodyPosition;

        // Update any legs that are currently stepping
        for (int i = 0; i < mNumLimbs; i++)
        {
            if (mIsStepping[i])
            {
                mStepProgress[i] += Time.deltaTime / stepDuration;

                if (mStepProgress[i] >= 1f)
                {
                    // Step complete
                    mStepProgress[i] = 1f;
                    mIsStepping[i] = false;
                    mSolver.limbs[i].target.position = mCurrentTarget[i];
                }
                else
                {
                    // Interpolate the step
                    float t = stepCurve.Evaluate(mStepProgress[i]);
                    Vector3 basePosition = Vector3.Lerp(mPreviousTarget[i], mCurrentTarget[i], t);

                    // Add vertical arc for the step
                    float heightOffset = Mathf.Sin(mStepProgress[i] * Mathf.PI) * stepHeight;
                    Vector3 stepPosition = basePosition + SurfaceNormal * heightOffset;

                    mSolver.limbs[i].target.position = stepPosition;
                }
            }
        }

        // Check if any leg needs to step (only if no other leg is stepping)
        bool anyLegStepping = false;
        for (int i = 0; i < mNumLimbs; i++)
        {
            if (mIsStepping[i])
            {
                anyLegStepping = true;
                break;
            }
        }

        if (!anyLegStepping)
        {
            // Find which leg needs to step the most
            int legToStep = -1;
            float maxDistance = threshold;

            for (int i = 0; i < mNumLimbs; i++)
            {
                float distance = (mCurrentTarget[i] - mTargetTransforms[i].position).magnitude;

                // Prefer alternating legs - give bonus to opposite leg
                float distanceWithPreference = distance;
                if (mLastSteppedLeg != -1 && i != mLastSteppedLeg)
                {
                    distanceWithPreference *= 1.2f; // 20% preference for alternating
                }

                if (distanceWithPreference > maxDistance)
                {
                    maxDistance = distanceWithPreference;
                    legToStep = i;
                }
            }

            // Start stepping with the leg that needs it most
            if (legToStep != -1)
            {
                mPreviousTarget[legToStep] = mCurrentTarget[legToStep];
                mCurrentTarget[legToStep] = mTargetTransforms[legToStep].position;
                mIsStepping[legToStep] = true;
                mStepProgress[legToStep] = 0f;
                mLastSteppedLeg = legToStep;
            }
        }
    }
}