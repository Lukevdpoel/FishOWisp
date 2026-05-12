using UnityEngine;

public class SceneTrigger : MonoBehaviour
{
    public string sceneName;

    [Tooltip("The ID of the SpawnPoint in the NEXT scene where the player should appear.")]
    public string targetSpawnID;

    private bool fired;

    private void OnTriggerEnter(Collider other)
    {
        if (fired) return;

        // Require the actual player root, not a child collider that may share the Player tag.
        Transform root = other.attachedRigidbody != null ? other.attachedRigidbody.transform : other.transform.root;
        if (!root.CompareTag("Player")) return;

        Debug.Log($"[SceneTrigger] '{gameObject.name}' fired by '{other.name}' (root '{root.name}') at player pos {root.position}. Trigger world pos {transform.position}, distance {Vector3.Distance(root.position, transform.position):F2}m. Loading scene '{sceneName}'.", this);

        fired = true;
        SceneTransitionManager.Instance.LoadScene(sceneName, targetSpawnID);
    }
}