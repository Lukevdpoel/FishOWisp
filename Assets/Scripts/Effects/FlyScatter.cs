using UnityEngine;

public class FlyScatter : MonoBehaviour
{
    public ParticleSystem flySystem;
    public Transform player;

    [Header("Flee Settings")]
    public float fleeDistance = 3.0f; // Distance from player to trigger flee
    public float fleeStrength = 10.0f; // How fast they run away

    [Header("Return Settings")]
    public float tetherRadius = 1.5f; // How far they can go before being pulled back
    public float returnStrength = 5.0f; // How strongly they are pulled back to the flower
    public float drag = 2.0f; // Slows them down so they don't shoot past the flower

    private ParticleSystem.Particle[] particles;

    void Update()
    {
        if (player == null) return;

        int numParticles = flySystem.particleCount;
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[numParticles];

        flySystem.GetParticles(particles);

        // Convert player position to Local Space so it matches the particles
        Vector3 playerLocalPos = transform.InverseTransformPoint(player.position);

        for (int i = 0; i < numParticles; i++)
        {
            Vector3 particlePos = particles[i].position;
            float distToPlayer = Vector3.Distance(particlePos, playerLocalPos);

            // 1. FLEE: If Player is close, push away
            if (distToPlayer < fleeDistance)
            {
                Vector3 fleeDir = (particlePos - playerLocalPos).normalized;
                particles[i].velocity += fleeDir * fleeStrength * Time.deltaTime;
            }
            // 2. RETURN: If Player is far, check if fly is too far from flower center
            else
            {
                float distFromCenter = particlePos.magnitude; // Distance from (0,0,0) local

                if (distFromCenter > tetherRadius)
                {
                    // Pull back towards center
                    Vector3 returnDir = -particlePos.normalized;
                    particles[i].velocity += returnDir * returnStrength * Time.deltaTime;

                    // Apply "Drag" to stop them from orbiting forever
                    particles[i].velocity -= particles[i].velocity * drag * Time.deltaTime;
                }
            }
        }

        flySystem.SetParticles(particles, numParticles);
    }
}