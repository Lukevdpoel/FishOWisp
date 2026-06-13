using Sirenix.OdinInspector;
using UnityEngine;

// Scene-singleton base. Awake claims the static slot if it's free; if a duplicate already
// exists, the duplicate's GameObject is destroyed. OnDestroy clears the static when the
// owning instance dies. Instance returns null if no live instance has registered yet —
// callers MUST null-check.
//
// Critical: this class deliberately does NOT auto-create a GameObject when Instance is
// null. The previous implementation did, which caused a destructive race: if any other
// MonoBehaviour's OnDestroy accessed Instance during play-mode shutdown, the getter would
// spawn a new GameObject (Unity warns "Some objects were not cleaned up..."). That ghost
// GameObject survived into the next play session, where the real instance's Awake then
// saw it as a duplicate and destroyed ITSELF. The symptom was that PlayerInventory /
// BaitInventory / UIManager / InventoryUI all worked once, then silently disappeared in
// the second play session.
public class GenericSingleton<T> : SerializedMonoBehaviour
    where T : Component
{
    private static T _instance;

    public static T Instance => _instance;

    // Cheap null-check identical to `Instance != null`. Kept for callers that want to be
    // explicit that they are NOT triggering a scene scan (since none happens here anyway).
    public static bool IsAvailable => _instance != null;

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            return;
        }

        // A real, live duplicate. First-instance-wins; drop the late arrival.
        // Unity's null-overloaded == treats destroyed objects as null, so this branch
        // only fires when the existing instance is a genuinely live MonoBehaviour.
        if (_instance != this)
        {
            // Destroying the GameObject takes EVERY component on it down — and manager
            // objects in this project stack many systems on one GameObject (the PlayerMaster
            // UI object hosts InventoryUI, NoteMenu, DialogueManager, PlayerInventory...).
            // A stray duplicate elsewhere in the scene winning the Awake race silently
            // killed all of those once. Log loudly so the next stray is found in seconds.
            Debug.LogError(
                $"[GenericSingleton] Duplicate {typeof(T).Name} detected — destroying GameObject " +
                $"'{gameObject.name}' AND every other component on it. The surviving instance is on " +
                $"'{_instance.gameObject.name}'. If the destroyed object hosted real systems, delete " +
                $"the stray duplicate {typeof(T).Name} from the scene instead.", _instance);
            Destroy(gameObject);
        }
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
