using UnityEngine;

/// <summary>
/// A simple character controller to move the GameObject using WASD keys and rotate with Q/E.
/// It also exposes the current input state and desired velocity.
/// </summary>
[RequireComponent(typeof(LegStepper))]
public class SimpleController : MonoBehaviour
{
    [Tooltip("The speed at which the object moves.")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("The speed at which the object sprints.")]
    [SerializeField] private float sprintSpeed = 10f;
    [Tooltip("The speed at which the object rotates in degrees per second.")]
    [SerializeField] private float rotationSpeed = 100f;

    /// <summary>
    /// True if there is any movement or rotation input this frame.
    /// </summary>
    public bool HasInput { get; private set; }

    /// <summary>
    /// True if the sprint input is currently active.
    /// </summary>
    public bool IsSprinting { get; private set; }

    /// <summary>
    /// The current intended movement speed based on input.
    /// </summary>
    public float CurrentLinearSpeed { get; private set; }

    /// <summary>
    /// The component of the velocity in the spider's forward direction.
    /// </summary>
    public float ForwardSpeed { get; private set; }

    /// <summary>
    /// The current intended rotation speed based on input.
    /// </summary>
    public float CurrentAngularSpeed { get; private set; }

    /// <summary>
    /// The calculated velocity based on player input.
    /// </summary>
    public Vector3 Velocity { get; private set; }

    private LegStepper legStepper;

    void Awake()
    {
        legStepper = GetComponent<LegStepper>();
    }

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

        // Check for sprint input (must be moving forward)
        IsSprinting = Input.GetKey(KeyCode.LeftShift) && verticalInput > 0;

        // --- State Update ---
        // Determine if there is any input from the player
        HasInput = horizontalInput != 0f || verticalInput != 0f || rotationInput != 0f;

        // Determine the current speed based on whether we are sprinting or not
        float currentMoveSpeed = IsSprinting ? sprintSpeed : moveSpeed;

        // Calculate intended speeds based on input
        Vector3 moveVector = new Vector3(horizontalInput, 0, verticalInput);
        CurrentLinearSpeed = moveVector.magnitude > 0 ? currentMoveSpeed : 0f;
        CurrentAngularSpeed = Mathf.Abs(rotationInput) > 0 ? rotationSpeed : 0f;

        // --- Movement Calculation ---
        // Get the body's "up" vector, which is aligned with the surface normal
        Vector3 up = transform.up;
        // Get the raw forward vector and project it onto the surface plane to get a pure forward direction
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        // Calculate the right vector using a cross product
        Vector3 right = Vector3.Cross(up, forward);

        // Calculate the final move direction relative to the surface-aligned axes
        Vector3 moveDirection = (forward * verticalInput + right * horizontalInput).normalized;
        // Set the public Velocity property for the LegStepper to use, applying the correct speed
        Velocity = moveDirection * currentMoveSpeed;

        // Calculate the forward speed component, ensuring it's not negative (for backward movement)
        ForwardSpeed = Mathf.Max(0, Vector3.Dot(Velocity, forward));

        // Apply rotation around the body's "up" axis
        transform.Rotate(up, rotationInput * rotationSpeed * Time.deltaTime, Space.World);

        // --- Debug Visualization ---
        // Draw the surface normal / body up vector
        Debug.DrawLine(transform.position, transform.position + up * 1.5f, Color.green);
        // Draw the calculated forward direction on the surface
        Debug.DrawLine(transform.position, transform.position + forward, Color.cyan);
        // Draw the calculated right direction on the surface
        Debug.DrawLine(transform.position, transform.position + right, Color.yellow);
        // Draw the final movement direction
        Debug.DrawLine(transform.position, transform.position + moveDirection * 2f, Color.red);
    }
}