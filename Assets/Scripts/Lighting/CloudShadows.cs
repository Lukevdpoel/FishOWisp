using UnityEngine;

[ExecuteAlways]
public class CloudShadows : MonoBehaviour
{
    public Light dirLight;
    public Vector2 windSpeed = new Vector2(0.5f, 0.1f);

    public bool enableBreathing = true;
    public float baseSize = 20f;
    public float amplitude = 2f;
    public float pulseSpeed = 0.5f;

    private float offsetLimit = 10000f;

    void Update()
    {
        if (dirLight == null) return;

        float xOffset = (Time.time * windSpeed.x) % offsetLimit;
        float yOffset = (Time.time * windSpeed.y) % offsetLimit;

        transform.position = new Vector3(xOffset, transform.position.y, yOffset);

        if (enableBreathing)
        {
            float wave = Mathf.Sin(Time.time * pulseSpeed);
            dirLight.cookieSize = baseSize + (wave * amplitude);
        }
    }
}