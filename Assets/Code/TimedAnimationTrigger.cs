using UnityEngine;

public class TimedAnimationTrigger : MonoBehaviour
{
    public Animator animator;
    public string triggerName = "Play"; // Use a trigger parameter in Animator
    public float intervalSeconds = 240f; // 4 minutes = 240 seconds

    private float timer;

    void Start()
    {
        timer = intervalSeconds;
    }

    void Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            animator.SetTrigger(triggerName);
            timer = intervalSeconds;
        }
    }
}
