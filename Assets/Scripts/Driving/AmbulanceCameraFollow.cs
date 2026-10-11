using UnityEngine;

[DisallowMultipleComponent]
public sealed class AmbulanceCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset;
    [SerializeField, Min(0.01f)] private float smoothTime = 0.15f;

    private Vector3 smoothVelocity;

    private void OnEnable()
    {
        smoothVelocity = Vector3.zero;
    }

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("AmbulanceCameraFollow requires its Ambulance target.", this);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (target == null || Time.timeScale <= 0f)
        {
            return;
        }

        transform.position = Vector3.SmoothDamp(transform.position, target.position + offset,
            ref smoothVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
    }

    public void Configure(Transform target)
    {
        this.target = target;
        offset = transform.position - target.position;
        smoothVelocity = Vector3.zero;
    }
}
