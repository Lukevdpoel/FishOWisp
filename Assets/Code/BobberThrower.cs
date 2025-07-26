using UnityEngine;
using UnityEngine.UI;

public class ObjectThrower : MonoBehaviour
{
    private bool readyToThrow = true;
    private bool bobberInWater = false;
    private bool isCharging = false;
    private bool isReelingIn = false;
    private float currentThrowForce;
    private Vector3 throwDirection;

    [Header("Gameplay")]
    public GameObject objectToHide;

    [Header("Throwing")]
    public GameObject throwablePrefab;
    public Transform throwPoint;
    public float minThrowForce = 5f;
    public float maxThrowForce = 20f;
    public float chargeRate = 10f;

    [Header("Cast Line")]
    public LineToThrownObject castLine;

    [Header("UI")]
    public Slider chargeSlider;
    public GameObject bobberIndicator;

    [Header("Aim")]
    public LayerMask aimLayerMask;

    [Header("Animation")]
    public Animator animator;
    public string chargingAnimTrigger = "StartCharging";
    public string throwAnimTrigger = "Throw";
    public string reelInAnimTrigger = "ReelIn";
    public float reelInAnimationTime = 0.6f;

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
            Debug.LogWarning("Animator not assigned.");
    }

    void Update()
    {
        // Left Click (Down)
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            if (isReelingIn)
            {
                Debug.Log("Blocked: Reeling in...");
                return;
            }

            if (bobberInWater)
            {
                Debug.Log("Left click while bobber in water → Reel in");
                ReelInAndReset();
                return;
            }

            if (readyToThrow)
            {
                Debug.Log("Start charging...");
                StartCharging();
                return;
            }
        }

        // Left Click (Held)
        if (Input.GetKey(KeyCode.Mouse0) && isCharging)
        {
            ChargeThrow();
            UpdateDirectionFromMouse();
            RotatePlayerToDirection();
            UpdateBobber();
        }

        // Left Click (Released)
        if (Input.GetKeyUp(KeyCode.Mouse0) && isCharging)
        {
            Debug.Log("Throwing object...");
            ThrowObject();
        }
    }

    public void NotifyBobberInWater()
    {
        Debug.Log("✅ NotifyBobberInWater CALLED");

        bobberInWater = true;

        if (objectToHide != null)
            objectToHide.SetActive(false);
    }


    void StartCharging()
    {
        if (bobberInWater || isReelingIn)
        {
            Debug.LogWarning("Blocked StartCharging — bobberInWater or isReelingIn");
            return;
        }

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
            rb.velocity = Vector3.zero;
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
        Vector3 point;
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, aimLayerMask))
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

    void ThrowObject()
    {
        if (bobberInWater || isReelingIn)
        {
            Debug.LogWarning("Blocked ThrowObject — bobberInWater or isReelingIn");
            return;
        }

        GameObject thrownObject = Instantiate(throwablePrefab, throwPoint.position, Quaternion.Euler(180f, 0f, 0f));

        // Update: assign thrower to Bobber script
        Bobber bobberScript = thrownObject.GetComponent<Bobber>();
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
                Debug.LogWarning("LineAnchor not found, falling back.");
                castLine.AttachTo(thrownObject.transform);
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

    void ReelInAndReset()
    {
        Debug.Log("Reeling in...");

        isReelingIn = true;
        readyToThrow = false;

        if (animator != null && !string.IsNullOrEmpty(reelInAnimTrigger))
        {
            animator.SetTrigger(reelInAnimTrigger);
        }

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
            castLine.Detach();

        if (characterController != null)
            characterController.enabled = true;

        if (rb != null)
            rb.isKinematic = false;

        if (objectToHide != null)
            objectToHide.SetActive(true);

        isCharging = false;
        currentThrowForce = 0f;
        bobberInWater = false;

        Invoke(nameof(FinishReelIn), reelInAnimationTime);
    }

    void FinishReelIn()
    {
        Debug.Log("Finished reeling. Ready to throw.");
        isReelingIn = false;
        readyToThrow = true;
    }
}
