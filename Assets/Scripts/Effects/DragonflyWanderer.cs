using UnityEngine;

public class DragonflyWanderer : MonoBehaviour
{
    [Header("Roam Area")]
    public Transform areaCenter;
    public Vector3 areaSize = new Vector3(8f, 1.5f, 8f);
    public float minHeightAboveAnchor = 0.3f;

    [Header("Motion")]
    [Tooltip("SmoothDamp time. Lower = snappier darts.")]
    public float dartSmoothTime = 0.18f;
    public float maxSpeed = 6f;
    public Vector2 hoverTimeRange = new Vector2(0.2f, 1.2f);
    public Vector2 arriveDistance = new Vector2(0.15f, 0.3f);

    [Header("Feel")]
    public float bobAmplitude = 0.05f;
    public float bobFrequency = 6f;
    public float turnSpeed = 8f;

    Vector3 _target;
    Vector3 _velocity;
    float _hoverUntil;
    float _bobPhase;

    void Start()
    {
        _bobPhase = Random.value * Mathf.PI * 2f;
        PickNewTarget();
    }

    void Update()
    {
        Vector3 bob = Vector3.up * Mathf.Sin(Time.time * bobFrequency + _bobPhase) * bobAmplitude;
        Vector3 goal = _target + bob;

        transform.position = Vector3.SmoothDamp(
            transform.position, goal, ref _velocity, dartSmoothTime, maxSpeed);

        if (_velocity.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(_velocity.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * turnSpeed);
        }

        float d = Vector3.Distance(transform.position, _target);
        if (d < Random.Range(arriveDistance.x, arriveDistance.y) && Time.time >= _hoverUntil)
        {
            _hoverUntil = Time.time + Random.Range(hoverTimeRange.x, hoverTimeRange.y);
            PickNewTarget();
        }
    }

    void PickNewTarget()
    {
        Vector3 c = areaCenter ? areaCenter.position : transform.position;
        _target = c + new Vector3(
            Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
            Mathf.Max(minHeightAboveAnchor, Random.Range(0f, areaSize.y)),
            Random.Range(-areaSize.z * 0.5f, areaSize.z * 0.5f));
    }

    void OnDrawGizmosSelected()
    {
        Vector3 c = areaCenter ? areaCenter.position : transform.position;
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.4f);
        Gizmos.DrawWireCube(c + Vector3.up * areaSize.y * 0.5f, areaSize);
    }
}
