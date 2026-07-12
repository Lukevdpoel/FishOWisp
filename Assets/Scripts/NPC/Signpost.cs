using UnityEngine;

// A read-it-yourself signpost for playtesters: walk up, press interact, and it shows a titled
// blurb through the normal dialogue box (typewriter, page-through, camera focus and all). It reuses
// DialogueManager/Dialogue wholesale — the only difference from a DialogueNPC is that the title and
// text are typed straight into THIS component's inspector instead of authored as a separate Dialogue
// asset, so setting one up for a playtest is a two-field job with no asset to create.
//
// Setup: put this on the signpost object, give it a trigger collider (IsTrigger) sized to the
// approach area, optionally drag a world-space "press E" prompt into promptUI, then fill in the
// title and one string per page.
[RequireComponent(typeof(Collider))]
public class Signpost : MonoBehaviour
{
    [Header("Sign Content")]
    [Tooltip("Heading shown in the dialogue box's name row (e.g. \"How to Fish\").")]
    public string title = "Signpost";
    [Tooltip("The body text. Each entry is one page — the player pages through them like normal dialogue.")]
    [TextArea(3, 10)]
    public string[] pages = new string[] { "" };

    [Header("Interaction")]
    [Tooltip("Drag the world-space 'E' / interact prompt object here. Shown only while the player is in range.")]
    public GameObject promptUI;

    private bool playerInZone;
    // Built lazily from title/pages on first read so inspector tweaks during a playtest are picked
    // up, and torn-down runtime instance never litters the project as an asset.
    private Dialogue runtimeDialogue;

    private void Start()
    {
        if (promptUI != null) promptUI.SetActive(false);
    }

    private void Update()
    {
        if (!playerInZone) return;
        // Reading waits until any charge jump has fully landed (hint hidden too).
        if (PlayerController.IsPlayerJumping) return;
        // Tell the HUD prompt bar an interact is available (not while already reading).
        if (DialogueManager.Instance != null && !DialogueManager.Instance.IsDialogueActive())
            InteractHint.Ping();
        if (!(KeyInput.InteractPressed || GamepadInput.InteractPressed)) return;

        if (DialogueManager.Instance != null && !DialogueManager.Instance.IsDialogueActive())
        {
            DialogueManager.Instance.StartDialogue(BuildDialogue(), transform);
        }
    }

    private Dialogue BuildDialogue()
    {
        if (runtimeDialogue == null) runtimeDialogue = ScriptableObject.CreateInstance<Dialogue>();

        int count = pages != null ? pages.Length : 0;
        if (count == 0) count = 1;
        runtimeDialogue.lines = new DialogueLine[count];
        for (int i = 0; i < count; i++)
        {
            runtimeDialogue.lines[i] = new DialogueLine
            {
                characterName = title,
                text = pages != null && i < pages.Length ? pages[i] : ""
            };
        }
        runtimeDialogue.isQuestion = false;
        return runtimeDialogue;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInZone = true;
        if (promptUI != null) promptUI.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInZone = false;
        if (promptUI != null) promptUI.SetActive(false);

        // Player walked off mid-read — close the box like the NPCs do.
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.ForceCloseAllUI();
        }
    }

    private void OnDestroy()
    {
        if (runtimeDialogue != null) Destroy(runtimeDialogue);
    }
}
