using UnityEngine;
using UnityEngine.UI;

public class ObjectThrower : MonoBehaviour
{
    [Header("Gameplay")]
    public GameObject objectToHide; // Drag the object to hide here

    [Header("Throwing")]
    public GameObject throwablePrefab;
    public Transform throwPoint;
    public float minThrowForce = 5f;
    public float maxThrowForce = 20f;
    public float chargeRate = 10f;

    [Header("Cast Line")]
    public LineToThrownObject castLine; // Drag the CastPoint here in Inspector

    public void NotifyBobberInWater()
    {
        bobberInWater = true;

        if (objectToHide != null)
            objectToHide.SetActive(false);
    }


    [Header("UI")]
    public Slider chargeSlider;
    public GameObject bobberIndicator;

    [Header("Aim")]
    public LayerMask aimLayerMask;

    [Header("Animation")]
    public Animator animator;
    public string chargingAnimTrigger = "StartCharging";
    public string throwAnimTrigger = "Throw";

    private bool bobberInWater = false;
    private float currentThrowForce;
    private bool isCharging;
    private Vector3 throwDirection;

    private CharacterController characterController;
    private Rigidbody rb;
    private Transform playerTransform;

    private GameObject activeBobber;

    void Start()
    {
        playerTransform = transform;

        if (chargeSlider != null)
        {
            chargeSlider.minValue = minThrowForce;
            chargeSlider.maxValue = maxThrowForce;
            chargeSlider.value = minThrowForce;
            chargeSlider.gameObject.SetActive(false);
        }

        if (bobberIndicator != null)
            bobberIndicator.SetActive(false);

        characterController = GetComponent<CharacterController>();
        rb = GetComponent<Rigidbody>();

        if (animator == null)
            Debug.LogWarning("Animator not assigned to ObjectThrower.");
    }

    void Update()
    {
        // ✅ Q to reset ONLY if bobber is in water
        if (Input.GetKeyDown(KeyCode.Q) && bobberInWater)
        {
            ResetBobber();
            return;
        }

        // ✅ Left-click: Start charging if bobber is NOT in water
        if (Input.GetKeyDown(KeyCode.Mouse0) && !bobberInWater)
        {
            StartCharging();
        }

        // ✅ Continue charging if holding left-click
        if (Input.GetKey(KeyCode.Mouse0) && isCharging)
        {
            ChargeThrow();
            UpdateDirectionFromMouse();
            RotatePlayerToDirection();
            UpdateBobber();
        }

        // ✅ Release to throw
        if (Input.GetKeyUp(KeyCode.Mouse0) && isCharging)
        {
            ThrowObject();
        }
    }



    void StartCharging()
    {
        isCharging = true;
        currentThrowForce = minThrowForce;

        if (chargeSlider != null)
            chargeSlider.gameObject.SetActive(true);

        if (bobberIndicator != null)
            bobberIndicator.SetActive(true);

        if (characterController != null)
            characterController.enabled = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (activeBobber != null)
        {
            Destroy(activeBobber);
            activeBobber = null;
        }

        if (animator != null && !string.IsNullOrEmpty(chargingAnimTrigger))
        {
            animator.SetTrigger(chargingAnimTrigger);
        }
    }

    void ChargeThrow()
    {
        currentThrowForce += chargeRate * Time.deltaTime;
        currentThrowForce = Mathf.Min(currentThrowForce, maxThrowForce);

        if (chargeSlider != null)
            chargeSlider.value = currentThrowForce;
    }

    void UpdateDirectionFromMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        Vector3 point;
        if (Physics.Raycast(ray, out hit, 100f, aimLayerMask))
        {
            point = hit.point;
        }
        else
        {
            point = ray.origin + ray.direction * 50f;
        }

        Vector3 dir = (point - throwPoint.position);
        dir.y = 0f;
        throwDirection = dir.normalized;
    }

    void RotatePlayerToDirection()
    {
        if (throwDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(throwDirection);
            playerTransform.rotation = Quaternion.Lerp(playerTransform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }

    void UpdateBobber()
    {
        if (bobberIndicator != null)
        {
            Vector3 predictedPoint = throwPoint.position + throwDirection * currentThrowForce;
            bobberIndicator.transform.position = predictedPoint + Vector3.up * 0.1f;
        }
    }
    void ResetBobber()
    {
        if (activeBobber != null)
        {
            Destroy(activeBobber);
            activeBobber = null;
        }

        if (chargeSlider != null)
            chargeSlider.gameObject.SetActive(false);

        if (bobberIndicator != null)
            bobberIndicator.SetActive(false);

        if (castLine != null)
            castLine.Detach(); // If applicable in your implementation

        if (characterController != null)
            characterController.enabled = true;

        if (rb != null)
            rb.isKinematic = false;

        if (objectToHide != null)
            objectToHide.SetActive(true);


        isCharging = false;
        currentThrowForce = 0f;
        bobberInWater = false;
    }

    void ThrowObject()
    {
        GameObject thrownObject = Instantiate(throwablePrefab, throwPoint.position, Quaternion.Euler(180f, 0f, 0f));

        // 🔁 Set reference back to this ObjectThrower
        BobberThrowable bobberScript = thrownObject.GetComponent<BobberThrowable>();
        if (bobberScript != null)
        {
            bobberScript.thrower = this;
        }

        Rigidbody thrownRb = thrownObject.GetComponent<Rigidbody>();
        if (thrownRb != null)
        {
            thrownRb.AddForce(throwDirection * currentThrowForce, ForceMode.VelocityChange);
        }

        activeBobber = thrownObject;

        if (castLine != null)
        {
            Transform anchor = thrownObject.transform.Find("LineAnchor");
            if (anchor != null)
            {
                castLine.AttachTo(anchor);
            }
            else
            {
                Debug.LogWarning("LineAnchor child not found on thrown object.");
                castLine.AttachTo(thrownObject.transform); // fallback
            }
        }

        if (chargeSlider != null)
        {
            chargeSlider.value = minThrowForce;
            chargeSlider.gameObject.SetActive(false);
        }

        if (bobberIndicator != null)
            bobberIndicator.SetActive(false);

        if (characterController != null)
            characterController.enabled = true;

        if (rb != null)
            rb.isKinematic = false;

        isCharging = false;
        currentThrowForce = 0f;

        if (animator != null && !string.IsNullOrEmpty(throwAnimTrigger))
        {
            animator.SetTrigger(throwAnimTrigger);
        }
    }

}
