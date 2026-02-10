using System.Collections;
using UnityEngine;
using TMPro;

public class DialogueManager : MonoBehaviour
{
    [Header("Dialogue UI")]
    public GameObject dialogueBox;
    public TMP_Text nameText;
    public TMP_Text dialogueText;

    [Header("Open/Close Animation")]
    public float openDuration = 0.3f;
    public AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Pulse Settings (Next Line)")]
    public float pulseDuration = 0.15f;
    public float pulseScale = 0.92f;

    [Header("Typewriter")]
    public float typingSpeed = 0.02f;
    public AudioSource typingAudio;

    [Header("Choice UI")]
    public GameObject choicePanel;
    public RectTransform arrow;
    public RectTransform yesOption;
    public RectTransform noOption;

    [Header("Events")]
    public GameObject specialUI;

    private Dialogue currentDialogue;
    private int currentLineIndex = 0;
    private bool isDialogueActive = false;
    private bool isTyping = false;
    private bool isChoosing = false;
    private int selectedOption = 0;

    public static DialogueManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Hide UI on start
        if (dialogueBox)
        {
            dialogueBox.SetActive(false);
            dialogueBox.transform.localScale = Vector3.zero;
        }

        // 🟢 FIX 1: Clear text on load so "New Text" never flashes
        if (dialogueText) dialogueText.text = "";
        if (nameText) nameText.text = "";

        if (choicePanel) choicePanel.SetActive(false);
        if (specialUI) specialUI.SetActive(false);
    }

    void Update()
    {
        if (isChoosing)
        {
            HandleChoiceInput();
            return;
        }

        if (isDialogueActive)
        {
            // Input Check
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0))
            {
                if (isTyping)
                {
                    StopAllCoroutines();
                    // Ensure box is full size if we skip animation
                    dialogueBox.transform.localScale = Vector3.one;
                    dialogueText.text = currentDialogue.lines[currentLineIndex].text;
                    isTyping = false;
                }
                else
                {
                    StartCoroutine(PulseAndContinue());
                }
            }
        }
    }

    public void StartDialogue(Dialogue dialogue)
    {
        currentDialogue = dialogue;
        currentLineIndex = 0;
        isDialogueActive = true;
        isChoosing = false;

        // 🟢 FIX 2: Clear text INSTANTLY before the box starts opening
        dialogueText.text = "";
        if (nameText) nameText.text = "";

        dialogueBox.SetActive(true);
        dialogueBox.transform.localScale = Vector3.zero;

        // Open Animation
        StartCoroutine(AnimateBox(Vector3.one, openDuration, openCurve, () =>
        {
            DisplayLine();
        }));
    }

    // --- CORE DIALOGUE LOGIC ---

    private void DisplayLine()
    {
        DialogueLine line = currentDialogue.lines[currentLineIndex];
        if (nameText) nameText.text = line.characterName;

        StopCoroutine("TypeText");
        StartCoroutine(TypeText(line.text));
    }

    private void AdvanceLine()
    {
        currentLineIndex++;
        if (currentLineIndex < currentDialogue.lines.Length)
        {
            DisplayLine();
        }
        else
        {
            if (currentDialogue.isQuestion)
                ShowChoiceMenu();
            else
                CloseDialogue();
        }
    }

    // --- ANIMATIONS ---

    IEnumerator PulseAndContinue()
    {
        // 1. Shrink
        float halfTime = pulseDuration / 2f;
        Vector3 originalScale = Vector3.one;
        Vector3 targetScale = originalScale * pulseScale;

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / halfTime;
            dialogueBox.transform.localScale = Vector3.Lerp(originalScale, targetScale, t);
            yield return null;
        }

        // 2. Grow Back + Load Next Text
        AdvanceLine();

        t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / halfTime;
            dialogueBox.transform.localScale = Vector3.Lerp(targetScale, originalScale, t);
            yield return null;
        }

        dialogueBox.transform.localScale = originalScale;
    }

    IEnumerator AnimateBox(Vector3 targetScale, float duration, AnimationCurve curve, System.Action onComplete = null)
    {
        Vector3 startScale = dialogueBox.transform.localScale;
        float timeElapsed = 0f;

        while (timeElapsed < duration)
        {
            timeElapsed += Time.deltaTime;
            float t = timeElapsed / duration;
            float curveValue = curve.Evaluate(t);
            dialogueBox.transform.localScale = Vector3.LerpUnclamped(startScale, targetScale, curveValue);
            yield return null;
        }

        dialogueBox.transform.localScale = targetScale;
        onComplete?.Invoke();
    }

    IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = ""; // Clear again just to be safe
        foreach (char c in text)
        {
            dialogueText.text += c;
            if (typingAudio) typingAudio.Play();
            yield return new WaitForSeconds(typingSpeed);
        }
        isTyping = false;
    }

    private void CloseDialogue()
    {
        isTyping = false;
        isChoosing = false;
        if (choicePanel) choicePanel.SetActive(false);

        // 🟢 FIX 3: Clear text on close so it doesn't show up next time for 1 frame
        dialogueText.text = "";

        StartCoroutine(AnimateBox(Vector3.zero, 0.2f, AnimationCurve.Linear(0, 0, 1, 1), () =>
        {
            isDialogueActive = false;
            dialogueBox.SetActive(false);
            if (specialUI) specialUI.SetActive(false);
        }));
    }

    // --- CHOICE LOGIC ---
    private void ShowChoiceMenu()
    {
        isChoosing = true;
        selectedOption = 0;
        if (choicePanel) choicePanel.SetActive(true);
        UpdateArrowPosition();
    }

    private void HandleChoiceInput()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S))
        {
            selectedOption = 1 - selectedOption;
            UpdateArrowPosition();
        }

        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            ConfirmChoice();
        }
    }

    private void UpdateArrowPosition()
    {
        if (arrow && yesOption && noOption)
            arrow.position = selectedOption == 0 ? yesOption.position : noOption.position;
    }

    private void ConfirmChoice()
    {
        CloseDialogue();
        if (selectedOption == 0 && specialUI != null)
            specialUI.SetActive(true);
    }

    public bool IsDialogueActive() => isDialogueActive || isChoosing;

    public void ForceCloseAllUI()
    {
        StopAllCoroutines();
        dialogueBox.transform.localScale = Vector3.zero;
        dialogueBox.SetActive(false);
        isDialogueActive = false;
        isTyping = false;
        isChoosing = false;
        if (specialUI) specialUI.SetActive(false);
    }
}