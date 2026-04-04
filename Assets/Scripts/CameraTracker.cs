using UnityEngine;

public class CameraTracker : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private BoxCollider2D bounds;
    [SerializeField] private float followSpeed = 8f;
    [SerializeField] private float zoomSize = 80f;
    [SerializeField] private float smoothTime = 0.08f;

    private float minX;
    private float maxX;
    private float minY;
    private float maxY;
    private float halfHeight;
    private float halfWidth;
    private Vector3 followVelocity;
    private Rigidbody2D targetBody;

    public BoxCollider2D BoundsCollider => bounds;

    private void Start()
    {
        Camera cam = GetComponent<Camera>();
        if (target == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                target = playerObject.transform;
        }

        if (target != null)
            targetBody = target.GetComponent<Rigidbody2D>();

        if (cam == null)
            return;

        cam.orthographicSize = zoomSize;
        halfHeight = cam.orthographicSize;
        halfWidth = halfHeight * cam.aspect;

        if (bounds == null)
            return;

        Bounds b = bounds.bounds;

        minX = b.min.x + halfWidth;
        maxX = b.max.x - halfWidth;
        minY = b.min.y + halfHeight;
        maxY = b.max.y - halfHeight;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 targetPosition = targetBody != null
            ? new Vector3(targetBody.position.x, targetBody.position.y, target.position.z)
            : target.position;

        float x = targetPosition.x;
        float y = targetPosition.y;

        if (bounds != null)
        {
            x = Mathf.Clamp(x, minX, maxX);
            y = Mathf.Clamp(y, minY, maxY);
        }

        Vector3 desiredPosition = new Vector3(x, y, -10f);
        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            smoothTime,
            followSpeed * zoomSize);
    }
}
