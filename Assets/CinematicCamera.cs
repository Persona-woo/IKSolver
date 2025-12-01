using UnityEngine;

/// <summary>
/// A cinematic camera that smoothly follows a target, maintaining the initial relative offset and rotation set in the editor.
/// This script should be on a camera that is NOT a child of the target.
/// </summary>
public class CinematicCamera : MonoBehaviour
{
    [Tooltip("The target the camera will follow (the spider).")]
    public Transform target;

    [Header("Damping")]
    [Tooltip("How quickly the camera catches up to the target's position. Smaller values are slower and feel more 'dragged'.")]
    [Range(0.01f, 1.0f)]
    public float positionDamping = 0.15f;

    [Tooltip("How quickly the camera rotates to match the target's orientation. Smaller values are slower.")]
    [Range(0.01f, 1.0f)]
    public float rotationDamping = 0.1f;

    // The initial offset from the target, calculated in the target's local space.
    private Vector3 localPositionOffset;
    // The initial rotational offset from the target.
    private Quaternion rotationOffset;

    // Used by SmoothDamp to track the camera's current velocity.
    private Vector3 velocity = Vector3.zero;

    void Start()
    {
        if (target == null)
        {
            Debug.LogWarning("Cinematic Camera has no target assigned. The camera will not move.");
            return;
        }

        // Calculate the initial positional offset in the target's local space.
        // This ensures the offset rotates correctly with the target.
        localPositionOffset = Quaternion.Inverse(target.rotation) * (transform.position - target.position);

        // Calculate the initial rotational offset.
        rotationOffset = Quaternion.Inverse(target.rotation) * transform.rotation;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // --- Handle Position ---
        // Calculate the desired world position by transforming the local offset by the target's current rotation.
        Vector3 desiredPosition = target.position + (target.rotation * localPositionOffset);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, positionDamping);

        // --- Handle Rotation ---
        // Calculate the desired rotation by applying the initial rotational offset to the target's current rotation.
        Quaternion desiredRotation = target.rotation * rotationOffset;
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationDamping);
    }
}