using UnityEngine;

/// <summary>
/// A simple character controller to move the GameObject using WASD keys and rotate with Q/E.
/// It also exposes the current input state.
/// </summary>
public class SimpleController : MonoBehaviour
{
    [Tooltip("The speed at which the object moves.")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("The speed at which the object rotates in degrees per second.")]
    [SerializeField] private float rotationSpeed = 100f;

    /// <summary>
    /// True if there is any movement or rotation input this frame.
    /// </summary>
    public bool HasInput { get; private set; }

    /// <summary>
    /// The current intended movement speed based on input.
    /// </summary>
    public float CurrentLinearSpeed { get; private set; }

    /// <summary>
    /// The current intended rotation speed based on input.
    /// </summary>
    public float CurrentAngularSpeed { get; private set; }

    void Update()
    {
        // --- Input Gathering ---
        float horizontalInput = Input.GetAxis("Horizontal"); // AD
        float verticalInput = Input.GetAxis("Vertical");   // wS
        float rotationInput = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            rotationInput = -1f; // Rotate left
        }
        else if (Input.GetKey(KeyCode.E))
        {
            rotationInput = 1f; // Rotate right
        }

        // --- State Update ---
        // Determine if there is any input from the player
        HasInput = horizontalInput != 0f || verticalInput != 0f || rotationInput != 0f;

        // Calculate intended speeds based on input
        Vector3 moveVector = new Vector3(horizontalInput, 0, verticalInput);
        CurrentLinearSpeed = moveVector.magnitude > 0 ? moveSpeed : 0f;
        CurrentAngularSpeed = Mathf.Abs(rotationInput) > 0 ? rotationSpeed : 0f;

        // --- Movement Application ---
        Vector3 moveDirection = (transform.forward * verticalInput + transform.right * horizontalInput).normalized;
        transform.position += moveDirection * moveSpeed * Time.deltaTime;   

        // Apply rotation around the Y axis (upward axis)
        transform.Rotate(Vector3.up, rotationInput * rotationSpeed * Time.deltaTime);
    }
}
