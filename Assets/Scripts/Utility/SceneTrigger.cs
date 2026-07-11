using UnityEngine;

public class SceneTrigger : MonoBehaviour
{
    public string sceneName;

    [Tooltip("The ID of the SpawnPoint in the NEXT scene where the player should appear.")]
    public string targetSpawnID;

    private bool fired;
    // Armed by default so a normal walk-in fires immediately. Disarmed only when the player is
    // detected ON the collider during an inbound transition (the return spawn point can sit on the
    // entrance): then we wait for the player to step OUT (OnTriggerExit re-arms) before this trigger
    // may fire again, so they don't get bounced straight back through it.
    private bool armed = true;

    private static bool IsPlayer(Collider other, out Transform root)
    {
        // Require the actual player root, not a child collider that may share the Player tag.
        root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform.root;
        return root.CompareTag("Player");
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other, out _)) armed = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (fired) return;
        if (!IsPlayer(other, out Transform root)) return;

        // Instance can be null with "Disable Domain Reload" play mode (static state survives between
        // sessions and can go stale). Fall back to a live scene lookup before giving up, and never
        // hard-NRE: if there's genuinely no manager, log clearly and leave 'fired' false so a fixed
        // setup can retry on the next entry.
        SceneTransitionManager stm = SceneTransitionManager.Instance;
        if (stm == null) stm = FindFirstObjectByType<SceneTransitionManager>();
        if (stm == null)
        {
            Debug.LogError($"[SceneTrigger] '{gameObject.name}' wanted to load '{sceneName}' but no live SceneTransitionManager exists in the scene. No transition performed.", this);
            return;
        }

        // Bail WITHOUT latching while a transition is in progress: the inbound spawn teleport (and
        // its Physics.SyncTransforms) can fire this enter while the player is being placed on the
        // entrance. Latching here would refuse the LoadScene yet leave 'fired' true, killing the
        // trigger for the rest of this scene visit. Disarm so we don't bounce the player back the
        // instant the transition finishes — they must step out (OnTriggerExit) to re-arm it.
        if (stm.IsTransitioning)
        {
            armed = false;
            Debug.Log($"[SceneTrigger] '{gameObject.name}' ignored enter by '{other.name}' during transition; disarmed until exit.", this);
            return;
        }
        if (!armed)
        {
            Debug.Log($"[SceneTrigger] '{gameObject.name}' ignored enter by '{other.name}' (disarmed — player spawned on the trigger, waiting for exit).", this);
            return;
        }

        Debug.Log($"[SceneTrigger] '{gameObject.name}' fired by '{other.name}' (root '{root.name}') at player pos {root.position}. Trigger world pos {transform.position}, distance {Vector3.Distance(root.position, transform.position):F2}m. Loading scene '{sceneName}'.", this);

        fired = true;
        stm.LoadScene(sceneName, targetSpawnID);
    }
}