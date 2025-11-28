using UnityEngine;

/// <summary>
/// A simple character controller to move the GameObject using WASD keys and rotate with Q/E.
/// </summary>
public class SimpleController : MonoBehaviour
{
    [Tooltip("The speed at which the object moves.")]
    [SerializeField] private float moveSpeed = 5f;
    [Tooltip("The speed at which the object rotates in degrees per second.")]
    [SerializeField] private float rotationSpeed = 100f;

    void Update()
    {
        // --- Movement ---
        
        float horizontalInput = Input.GetAxis("Horizontal"); // A/D keys
        float verticalInput = Input.GetAxis("Vertical");   // W/S keys
        Vector3 moveDirection = (transform.forward * verticalInput + transform.right * horizontalInput).normalized;

        transform.position += moveDirection * moveSpeed * Time.deltaTime;   

        // --- Rotation ---
        float rotationInput = 0f;
        if (Input.GetKey(KeyCode.Q))
        {
            rotationInput = -1f; // Rotate left
        }
        else if (Input.GetKey(KeyCode.E))
        {
            rotationInput = 1f; // Rotate right
        }

        // Apply rotation around the Y axis (upward axis)
        transform.Rotate(Vector3.up, rotationInput * rotationSpeed * Time.deltaTime);
    }
}
