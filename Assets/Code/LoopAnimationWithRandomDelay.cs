using UnityEngine;
using System.Collections;

public class LoopAnimationWithRandomDelay : MonoBehaviour
{
    public Animator animator;             // Assign in Inspector or automatically in Awake
    public string triggerName = "Play";   // The name of the trigger in Animator
    public float minDelay = 1f;           // Minimum delay between animations
    public float maxDelay = 5f;           // Maximum delay between animations

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        StartCoroutine(PlayAnimationLoop());
    }

    IEnumerator PlayAnimationLoop()
    {
        while (true)
        {
            animator.SetTrigger(triggerName);

            // Optional: wait for animation to finish before delaying
            // If not needed, remove this and only use the delay below
            yield return new WaitForSeconds(animator.GetCurrentAnimatorStateInfo(0).length);

            float randomDelay = Random.Range(minDelay, maxDelay);
            yield return new WaitForSeconds(randomDelay);
        }
    }
}
