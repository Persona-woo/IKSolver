using Unity.VisualScripting;
using UnityEngine;

public class LegStepper : MonoBehaviour
{
    [Tooltip("Center of gravity to base leg positions on.")]
    public Transform root;
    [Tooltip("Distance away from root before leg should take a step.")]
    public float threshold;
    [Tooltip("How fast each step is taken.")]
    public float strideSpeed;
    [Tooltip("How high each leg raises with each step.")]
    public float strideHeight;

    [Tooltip("Tripod group index per leg. 0 = first tripod, 1 = second tripod.")]
    public int[] legGroup;

    private int mCurrentGaitPhase = 0; //Phase 0 or 1
    
    private IKSolver mSolver;
    private GameObject[] mLegTargets;
    private Vector3[] mCurrentPos; // original position of each leg (before starting a step)
    private Vector3[] mTargetPos; // target position of each leg (as step is being taken)
    private float[] mStepProgress; // Use a dedicated progress tracker for each leg
    private bool[] mStepping; // array of bools - true if a limb is already mid-step
    private int mNumLimbs;
    private bool mIsInitialized = false;

    void Start()
    {
        mSolver = GetComponent<IKSolver>();
        if (mSolver == null)
        {
            Debug.Log("IK Solver not found. Object must have IK Solver attached to it.");
            return;
        }
        mNumLimbs = mSolver.mNumLimbs;
        
        // Ensure legGroup has a valid size; if not, auto-generate a tripod pattern
        if (legGroup == null || legGroup.Length != mNumLimbs)
        {
            legGroup = new int[mNumLimbs];

            // Default tripod: legs 0,2,4 are group 0; legs 1,3,5 are group 1
            for (int i = 0; i < mNumLimbs; i++)
            {
                legGroup[i] = i % 2; // 0,1,0,1,0,1
            }

            //Debug.LogWarning("legGroup array size did not match mNumLimbs. Generated default tripod pattern (0,1,0,1,0,1). Adjust in inspector if needed.");
        }
        
        mLegTargets = new GameObject[mNumLimbs];
        mCurrentPos = new Vector3[mNumLimbs];
        mTargetPos = new Vector3[mNumLimbs];
        mStepProgress = new float[mNumLimbs]; // Initialize the progress tracker
        mStepping = new bool[mNumLimbs];

        for (int i = 0; i < mNumLimbs; i++)
        {
            GameObject target = new GameObject(mSolver.limbs[i].endEffector.name + "_LegStepperTarget");
            
            
            target.transform.SetParent(this.transform); 

           
            target.transform.localPosition = this.transform.InverseTransformPoint(mSolver.limbs[i].target.position);
            target.transform.rotation = mSolver.limbs[i].target.rotation;
            
            mLegTargets[i] = target;

            // TODO: hardcoded random offset for limbs, would lose it's effect once spider moves too fast and "jumps"
            mSolver.limbs[i].target.position += new Vector3(Random.Range(-threshold, threshold), 0, Random.Range(-threshold, threshold));

            mCurrentPos[i] = mSolver.limbs[i].target.position;
            mStepping[i] = false;
            mStepProgress[i] = 0f; // Initial progress is 0
        }
        mIsInitialized = true;
    }

    void Update()
    {
        if (!mIsInitialized) return;
        
        TryStartTripodStep();
        
        for (int i = 0; i < mNumLimbs; i++)
        {
            // if ((mLegTargets[i].transform.position - mCurrentPos[i]).magnitude > threshold)
            // {
            //     mStepping[i] = true;
            //     // Steps in opposite direction towards threshold (eg: when walking we step forward rather than right below us)
            //     mTargetPos[i] = mLegTargets[i].transform.position + (mLegTargets[i].transform.position - mCurrentPos[i]).normalized * threshold * 0.99f;
            //     // The starting point of the step is the current fixed position of the foot
            //     // Do NOT update mCurrentPos here. It serves as the true start of the Lerp.
            // }

            if (mStepping[i])
            {
                // Update the progress of the step based on speed and time
                mStepProgress[i] += strideSpeed * Time.deltaTime;

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
        
        //If no legs are stepping anymore, flip to the other tripod group
        bool anyStillStepping = false;
        for (int i = 0; i < mNumLimbs; i++)
        {
            if (mStepping[i])
            {
                anyStillStepping = true;
                break;
            }
        }
        
        if (!anyStillStepping)
        {
            mCurrentGaitPhase = 1 - mCurrentGaitPhase; // 0 -> 1, 1 -> 0
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
    
    /// <summary>
    /// Checks if the current tripod (mCurrentGaitPhase) should step.
    /// If yes, starts a step for all three legs in that tripod.
    /// </summary>
    private void TryStartTripodStep()
    {
        // Only start a new tripod if no legs are currently stepping
        bool anyStepping = false;
        for (int i = 0; i < mNumLimbs; i++)
        {
            if (mStepping[i])
            {
                anyStepping = true;
                break;
            }
        }

        if (anyStepping)
            return;

        // Check if this tripod group needs a step (any leg too far from its target)
        bool groupNeedsStep = false;
        const float epsilon = 0.0001f;

        for (int i = 0; i < mNumLimbs; i++)
        {
            if (legGroup[i] != mCurrentGaitPhase)
                continue;

            float dist = (mLegTargets[i].transform.position - mCurrentPos[i]).magnitude;
            if (dist > threshold)
            {
                groupNeedsStep = true;
                break;
            }
        }

        if (!groupNeedsStep)
            return;

        // Start a step for ALL legs in this tripod group
        for (int i = 0; i < mNumLimbs; i++)
        {
            if (legGroup[i] != mCurrentGaitPhase)
                continue;

            Vector3 offset = mLegTargets[i].transform.position - mCurrentPos[i];
            Vector3 dir;

            if (offset.sqrMagnitude > epsilon)
                dir = offset.normalized;
            else
                dir = root != null ? root.forward : Vector3.forward;

            mStepping[i] = true;
            mStepProgress[i] = 0f;

            mTargetPos[i] = mLegTargets[i].transform.position + dir * threshold * 0.99f;
        }
    }
    
}
