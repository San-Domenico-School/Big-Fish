using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BoatWaterFloater : MonoBehaviour
{
    [Header("Buoyancy Settings")]
    [Tooltip("Drag points under the boat hull here.")]
    public Transform[] floatPoints;
    public float depthBeforeSubmerged = 1f;
    public float displacementAmount = 3f;
    public float waterDrag = 0.99f;
    public float waterAngularDrag = 0.5f;

    [Header("Water Reference")]
    // Reference your water object (or IgniteCoders WaterSystem manager)
    public Transform waterSurface; 

    private Rigidbody rb;
    private int floatPointsSubmerged;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        floatPointsSubmerged = 0;

        for (int i = 0; i < floatPoints.Length; i++)
        {
            Transform point = floatPoints[i];
            
            // Get wave height at current point X/Z location
            float waveHeight = GetWaterHeightAtPosition(point.position);

            // Check how far below the wave surface the probe point is
            if (point.position.y < waveHeight)
            {
                float displacementMultiplier = Mathf.Clamp01((waveHeight - point.position.y) / depthBeforeSubmerged) * displacementAmount;
                
                // Apply upward buoyant force at the probe position
                rb.AddForceAtPosition(Vector3.up * Mathf.Abs(Physics.gravity.y) * displacementMultiplier, point.position, ForceMode.Acceleration);
                
                floatPointsSubmerged++;
            }
        }
    }

    /// <summary>
    /// Samples height from IgniteCoders Water System.
    /// Adjust method call to match exact version API.
    /// </summary>
    private float GetWaterHeightAtPosition(Vector3 position)
    {
        // Option A: If using IgniteCoders API / Gerstner Wave math calculation:
        // return WaterSystem.Instance.GetWaveHeight(position);

        // Option B: Generic fallback if referencing the water transform directly:
        if (waterSurface != null)
        {
            return waterSurface.position.y;
        }

        return 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Dampen velocity when touching water for smooth motion
        if (floatPointsSubmerged > 0)
        {
            rb.linearDamping = waterDrag;
            rb.angularDamping = waterAngularDrag;
        }
    }
}