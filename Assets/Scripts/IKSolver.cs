using UnityEngine;

public class IKSolver : MonoBehaviour
{
    [Header("Chain")]
    [Tooltip("Bones assigned from root (e.g. thigh) to end effector (e.g. foot).")]
    public Transform[] bones; // length must be >= 2

    [Header("Targets")]
    [Tooltip("Pole is optional but recommended for knee/elbow joints.")]
    public Transform target; // world-space foot target
    public Transform pole; // world-space knee/elbow direction helper (optional)

    [Header("Settings")]
    public bool enabled = true; // turn IK on and off
    public int iterations = 12;

    private int endIndex; // index of end effector

    void Awake()
    {
        // Ensure at least 2 bones assigned for IK to work
        if (bones == null || bones.Length < 2)
        {
            Debug.LogError("IK Fabrik Error: needs at least 2 bones in chain.");
            return;
        }

        endIndex = bones.Length - 1;
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
        // Put end effector onto target
        bones[endIndex].position = target.position;

        // For each next joint, move to the correct distance and direction away
        for (int i = endIndex - 1; i >= 0; i--)
        {

        }
    }
}
