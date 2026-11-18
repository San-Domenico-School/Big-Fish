using UnityEngine;


[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class ProceduralVoxelCollider : MonoBehaviour
{
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;

    // Called by: Self / Mesh Generation Script
    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
    }

    // Called by: Voxel Generator Script after building the mesh vertices/triangles
    public void UpdateCollider(Mesh newMesh)
    {
        if (newMesh == null)
        {
            return;
        }

        // Assign the mesh to the MeshFilter for rendering
        meshFilter.sharedMesh = newMesh;

        // Assign the exact same mesh to the MeshCollider for physics
        meshCollider.sharedMesh = null; // Reset to force physics engine update
        meshCollider.sharedMesh = newMesh;
    }
}