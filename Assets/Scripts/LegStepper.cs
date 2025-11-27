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

            mCurrentPos[i] = mSolver.limbs[i].target.position;
            mStepping[i] = false;
            mStepProgress[i] = 0f; // Initial progress is 0
        }
        mIsInitialized = true;
    }

    void Update()
    {
        for (int i = 0; i < mNumLimbs; i++)
        {
            if ((mLegTargets[i].transform.position - mCurrentPos[i]).magnitude > threshold && !mStepping[i])
            {
                mStepping[i] = true;
                mTargetPos[i] = mLegTargets[i].transform.position;
                // The starting point of the step is the current fixed position of the foot
                // Do NOT update mCurrentPos here. It serves as the true start of the Lerp.
            }

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
