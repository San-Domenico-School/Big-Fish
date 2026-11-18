using UnityEngine;

namespace BigFish
{
    /// <summary>
    /// Spawns the player's boat at this object's position when
    /// play starts, and makes the camera follow it.
    /// </summary>
    public class BoatSpawner : MonoBehaviour
    {
        public GameObject BoatPrefab;

        // Length of the boat model (FishingBoat is ~12 units)
        public float BoatLength = 12f;

        [Tooltip("How high the boat sits above the water. Raise it if the hull sinks in.")]
        public float BoatRideHeight = 0f;

        [Header("Camera")]
        public bool CameraFollows = true;
        [Tooltip("Camera to follow the boat with. Leave empty to use Camera.main.")]
        public Camera FollowCamera;
        public Vector3 CameraOffset = new Vector3(0f, 9f, -20f);
        public float CameraSmoothing = 4f;

        public BoatController Boat { get; private set; }

        private Transform _cam;

        void Start()
        {
            if (BoatPrefab == null)
            {
                Debug.LogError("BoatSpawner: no Boat Prefab assigned.", this);
                return;
            }

            GameObject go = Instantiate(
                BoatPrefab,
                transform.position,
                transform.rotation
            );

            go.name = "PlayerBoat";

            Boat = go.GetComponent<BoatController>();

            if (Boat == null)
                Boat = go.AddComponent<BoatController>();

            Boat.BoatLength = BoatLength;
            Boat.WaterLevel = transform.position.y;
            Boat.RideHeight = BoatRideHeight;

            Camera cam = FollowCamera != null ? FollowCamera : Camera.main;
            if (CameraFollows && cam != null)
            {
                _cam = cam.transform;
                SnapCamera();
            }
        }

        // ==============================
        // FOLLOW CAMERA
        // ==============================

        void SnapCamera()
        {
            _cam.position = DesiredCameraPos();
            _cam.LookAt(Boat.transform.position + Vector3.up * 2f);
        }

        Vector3 DesiredCameraPos()
        {
            // Offset is relative to the boat's flat heading
            float yaw = Boat.transform.eulerAngles.y;

            return Boat.transform.position +
                   Quaternion.Euler(0f, yaw, 0f) * CameraOffset;
        }

        void LateUpdate()
        {
            if (_cam == null || Boat == null)
                return;

            float k = 1f - Mathf.Exp(-CameraSmoothing * Time.deltaTime);

            _cam.position = Vector3.Lerp(
                _cam.position,
                DesiredCameraPos(),
                k
            );

            _cam.LookAt(Boat.transform.position + Vector3.up * 2f);
        }
    }
}
