using UnityEngine;

public class EyeLookRandomizer : MonoBehaviour
{
    [Header("Settings")]
    public float radius = 0.2f;
    public float moveSpeed = 15f;
    public float minWaitTime = 0.5f;
    public float maxWaitTime = 2.5f;
    [Range(0f, 1f)] public float centerLookProbability = 0.2f;

    [Header("Motion Settings")]
    [Tooltip("If the character moves faster than this, eyes will center.")]
    public float velocityThreshold = 0.1f;
    [Tooltip("Drag the Character's main GameObject here (the one that moves).")]
    public Transform characterTransform;

    [Header("References")]
    public Renderer eyeRenderer;
    public string propertyName = "_LookOffset";

    private Vector2 _currentLook;
    private Vector2 _targetLook;
    private float _timer;
    private Material _eyeMat;
    private Vector3 _lastPosition;

    void Start()
    {
        if (eyeRenderer == null) eyeRenderer = GetComponent<Renderer>();
        _eyeMat = eyeRenderer.material;

        // Assume this object's parent is the character if not assigned
        if (characterTransform == null) characterTransform = transform.root;

        _lastPosition = characterTransform.position;
        PickNewLookTarget();
    }

    void Update()
    {
        // 1. Calculate Velocity
        float currentSpeed = Vector3.Distance(characterTransform.position, _lastPosition) / Time.deltaTime;
        _lastPosition = characterTransform.position;

        // 2. Decide Target based on movement
        if (currentSpeed > velocityThreshold)
        {
            // Moving? Look straight ahead (Center)
            _targetLook = Vector2.zero;
            // Reset timer so we look around immediately when we stop
            _timer = 0;
        }
        else
        {
            // Stopped? Handle Random Timer
            _timer -= Time.deltaTime;
            if (_timer <= 0)
            {
                PickNewLookTarget();
            }
        }

        // 3. Smoothly animate the eyes
        _currentLook = Vector2.Lerp(_currentLook, _targetLook, Time.deltaTime * moveSpeed);
        _eyeMat.SetVector(propertyName, _currentLook);
    }

    void PickNewLookTarget()
    {
        _targetLook = Random.insideUnitCircle * radius;
        _timer = Random.Range(minWaitTime, maxWaitTime);
        if (Random.value < centerLookProbability) _targetLook = Vector2.zero;
    }
}