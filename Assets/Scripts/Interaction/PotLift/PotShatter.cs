using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PotShatter : MonoBehaviour
{
    [Header("Shatter")]
    [SerializeField] private GameObject shatteredPrefab;
    [SerializeField] private float burstForce = 2.5f;
    [SerializeField] private float burstUpBias = 0.4f;
    [SerializeField] private float burstRadius = 0.5f;

    [Header("Lifetime")]
    [SerializeField] private float linger = 1.0f;
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("Optional FX")]
    [SerializeField] private AudioClip shatterSfx;
    [SerializeField] private GameObject shatterVfx;

    private bool hasShattered;

    public void Shatter()
    {
        Shatter(transform.position);
    }

    public void Shatter(Vector3 impactPoint)
    {
        if (hasShattered) return;
        hasShattered = true;

        BaitDropper baitDropper = GetComponent<BaitDropper>();
        if (baitDropper != null) baitDropper.Drop(impactPoint);

        CurrencyDropper currencyDropper = GetComponent<CurrencyDropper>();
        if (currencyDropper != null) currencyDropper.Drop(impactPoint);

        if (shatteredPrefab == null)
        {
            Destroy(gameObject);
            return;
        }

        GameObject shards = Instantiate(shatteredPrefab, transform.position, transform.rotation);

        if (shatterVfx != null) Instantiate(shatterVfx, impactPoint, Quaternion.identity);
        if (shatterSfx != null) AudioSource.PlayClipAtPoint(shatterSfx, impactPoint);

        IgnorePlayerCollision(shards);
        ApplyBurst(shards, impactPoint);

        // Detach the coroutine runner from this pot since we destroy it next.
        ShatterRunner.Run(shards, linger, fadeDuration);

        Destroy(gameObject);
    }

    private void IgnorePlayerCollision(GameObject shards)
    {
        Transform playerRoot = null;

        PlayerController pc = Object.FindFirstObjectByType<PlayerController>();
        if (pc != null) playerRoot = pc.transform.root;

        if (playerRoot == null)
        {
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) playerRoot = tagged.transform.root;
        }

        if (playerRoot == null)
        {
            Debug.LogWarning("[PotShatter] No player found — shards may collide with player.");
            return;
        }

        Collider[] playerCols = playerRoot.GetComponentsInChildren<Collider>(true);
        Collider[] shardCols = shards.GetComponentsInChildren<Collider>(true);
        foreach (Collider sc in shardCols)
            foreach (Collider plc in playerCols)
                if (sc != null && plc != null)
                    Physics.IgnoreCollision(sc, plc, true);
    }

    private void ApplyBurst(GameObject shards, Vector3 impactPoint)
    {
        foreach (Rigidbody rb in shards.GetComponentsInChildren<Rigidbody>())
        {
            Vector3 dir = (rb.worldCenterOfMass - impactPoint).normalized + Vector3.up * burstUpBias;
            rb.AddForce(dir.normalized * burstForce, ForceMode.Impulse);
            rb.AddExplosionForce(burstForce, impactPoint, burstRadius, 0.1f, ForceMode.Impulse);
        }
    }

    private class ShatterRunner : MonoBehaviour
    {
        public static void Run(GameObject shards, float linger, float fade)
        {
            GameObject host = new GameObject("ShatterRunner");
            ShatterRunner runner = host.AddComponent<ShatterRunner>();
            runner.StartCoroutine(runner.FadeAndDestroy(shards, linger, fade));
        }

        private IEnumerator FadeAndDestroy(GameObject shards, float linger, float fade)
        {
            yield return new WaitForSeconds(linger);

            if (shards == null) { Destroy(gameObject); yield break; }

            Renderer[] renderers = shards.GetComponentsInChildren<Renderer>();
            List<Material> mats = new List<Material>();
            List<Color> startColors = new List<Color>();
            foreach (Renderer r in renderers)
            {
                foreach (Material m in r.materials)
                {
                    mats.Add(m);
                    startColors.Add(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color);
                }
            }

            foreach (Rigidbody rb in shards.GetComponentsInChildren<Rigidbody>())
                rb.isKinematic = true;
            foreach (Collider c in shards.GetComponentsInChildren<Collider>())
                c.enabled = false;

            float t = 0f;
            while (t < fade && shards != null)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(1f - t / fade);
                for (int i = 0; i < mats.Count; i++)
                {
                    Color c = startColors[i];
                    c.a = a * startColors[i].a;
                    if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", c);
                    else mats[i].color = c;
                }
                yield return null;
            }

            if (shards != null) Destroy(shards);
            Destroy(gameObject);
        }
    }
}
