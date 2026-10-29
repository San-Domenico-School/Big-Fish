using UnityEngine;
using UnityEngine.InputSystem; // Uses the new Input System package

public class SimplePlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    private Rigidbody rb;
    private Vector2 movementInput;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // This automatically catches WASD / Arrow keys using the New Input System's default keyboard setup
    private void OnMove(InputValue value)
    {
        movementInput = value.Get<Vector2>();
    }

    private void FixedUpdate()
    {
        Vector3 movement = new Vector3(movementInput.x, 0.0f, movementInput.y);
        rb.linearVelocity = new Vector3(movement.x * speed, rb.linearVelocity.y, movement.z * speed);
    }
}