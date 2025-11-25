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
    private bool[] mStepping; // array of bools - true if a limb is already mid-step
    private int mNumLimbs;

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
        mStepping = new bool[mNumLimbs];

        for (int i = 0; i < mNumLimbs; i++)
        {
            GameObject target = new GameObject(mSolver.limbs[i].endEffector.name + "_LegStepperTarget");
            target.transform.position = mSolver.limbs[i].target.position;
            target.transform.rotation = mSolver.limbs[i].target.rotation;

            target.transform.SetParent(this.transform); // parent target under solver
            mLegTargets[i] = target;

            mCurrentPos[i] = mSolver.limbs[i].target.position;
            mStepping[i] = false;
        }
    }

    void Update()
    {
        for (int i = 0; i < mNumLimbs; i++)
        {
            if ((mLegTargets[i].transform.position - mCurrentPos[i]).magnitude > threshold && !mStepping[i])
            {
                mCurrentPos[i] = mSolver.limbs[i].target.position;
                mTargetPos[i] = mLegTargets[i].transform.position;
                mStepping[i] = true;
            }
            else
            {
                mSolver.limbs[i].target.position = mCurrentPos[i];
            }
            if (mStepping[i])
            {
                Debug.Log("Step being taken");
                // Horizontal distance of each step
                mSolver.limbs[i].target.position = Vector3.Lerp(mCurrentPos[i], mTargetPos[i], strideSpeed);
                // Vertical height of each step

                if ((mTargetPos[i] - mSolver.limbs[i].target.position).magnitude < 0.001f)
                {
                    mSolver.limbs[i].target.position = mTargetPos[i];
                    mStepping[i] = false;
                }
            }
        }
    }
}
