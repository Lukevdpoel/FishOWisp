using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    [Header("Hierarchy Setup")]
    public GameObject dialogueBox;
    public TMP_Text nameText;
    public TMP_Text dialogueText; // MUST be inside a RectMask2D parent!

    [Header("Scroll Settings")]
    public int maxVisibleLines = 3;
    public float scrollSpeed = 0.2f;

    [Header("Animation")]
    public float openDuration = 0.3f;
    public AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float pulseDuration = 0.15f;
    public float pulseScale = 0.92f;

    [Header("Typewriter")]
    public float typingSpeed = 0.03f;
    public AudioSource typingAudio;

    [Header("Choice UI")]
    public GameObject choicePanel;
    public RectTransform arrow;
    public RectTransform yesOption;
    public RectTransform noOption;
    public GameObject specialUI;

    // State
    private Dialogue currentDialogue;
    private int currentLineIndex = 0;
    private bool isDialogueActive = false;
    private bool isTyping = false;
    private bool skipTyping = false;
    private bool isChoosing = false;
    private int selectedOption = 0;

    private RectTransform textRect;
    private float currentScrollY = 0;
    private int linesScrolled = 0; // Tracks how many lines we have pushed up

    public static DialogueManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (dialogueText) textRect = dialogueText.GetComponent<RectTransform>();
        ForceCloseAllUI();
    }

    void Update()
    {
        if (isDialogueActive)
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0))
            {
                // 1. If currently typing -> Instant Skip
                if (isTyping)
                {
                    skipTyping = true;
                    return;
                }

                // 2. If finished typing -> Pulse and Start Next Sentence
                if (!isTyping)
                {
                    StartCoroutine(PulseAndContinue());
                }
            }
        }

        if (isChoosing) HandleChoiceInput();
    }

    public void StartDialogue(Dialogue dialogue)
    {
        currentDialogue = dialogue;
        currentLineIndex = 0;
        isDialogueActive = true;
        isChoosing = false;

        // Reset Text & Scroll
        dialogueText.text = "";
        currentScrollY = 0;
        linesScrolled = 0;
        if (textRect) textRect.anchoredPosition = Vector2.zero;
        if (nameText) nameText.text = "";

        dialogueBox.SetActive(true);
        dialogueBox.transform.localScale = Vector3.zero;

        StartCoroutine(AnimateBox(Vector3.one, openDuration, openCurve, () =>
        {
            DisplayLine();
        }));
    }

    private void DisplayLine()
    {
        DialogueLine line = currentDialogue.lines[currentLineIndex];
        if (nameText) nameText.text = line.characterName;

        // Append new text. If not first line, add newline.
        string textToType = (currentLineIndex == 0) ? line.text : "\n" + line.text;

        StopCoroutine("TypeText");
        StartCoroutine(TypeText(textToType));
    }

    IEnumerator TypeText(string text)
    {
        isTyping = true;
        skipTyping = false;

        // Loop through the NEW text chunk character by character
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            dialogueText.text += c;
            dialogueText.ForceMeshUpdate(); // Update physics immediately

            // 🟢 AUTO-SCROLL LOGIC
            // If the total lines exceed what fits + what we already scrolled
            if (dialogueText.textInfo.lineCount > maxVisibleLines + linesScrolled)
            {
                TMP_LineInfo lineInfo = dialogueText.textInfo.lineInfo[0];
                float lineHeight = lineInfo.lineHeight;

                linesScrolled++;
                yield return StartCoroutine(ScrollTextUp(lineHeight));
            }

            // Audio
            if (typingAudio && !char.IsWhiteSpace(c)) typingAudio.Play();

            // 🟢 SKIP LOGIC (FIXED)
            if (skipTyping)
            {
                // We are at index 'i'. We need to add from 'i + 1' to end.
                if (i + 1 < text.Length)
                {
                    string remaining = text.Substring(i + 1);
                    dialogueText.text += remaining;
                    dialogueText.ForceMeshUpdate();
                }

                // Snap Scroll to Bottom
                int totalLines = dialogueText.textInfo.lineCount;
                if (totalLines > maxVisibleLines)
                {
                    float lineHeight = dialogueText.textInfo.lineInfo[0].lineHeight;
                    int linesToHide = totalLines - maxVisibleLines;

                    float targetY = linesToHide * lineHeight;
                    textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, targetY);

                    // Sync variables
                    currentScrollY = targetY;
                    linesScrolled = linesToHide;
                }
                break; // Exit the loop
            }

            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
    }

    IEnumerator ScrollTextUp(float distance)
    {
        float t = 0;
        float startY = textRect.anchoredPosition.y;
        float targetY = startY + distance;
        currentScrollY = targetY;

        while (t < 1f)
        {
            t += Time.deltaTime / scrollSpeed;
            textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, Mathf.Lerp(startY, targetY, t));
            yield return null;
        }
        textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, targetY);
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
            if (currentDialogue.isQuestion) ShowChoiceMenu();
            else CloseDialogue();
        }
    }

    // --- ANIMATIONS & UTILS ---

    IEnumerator PulseAndContinue()
    {
        float halfTime = pulseDuration / 2f;
        Vector3 start = Vector3.one;
        Vector3 target = start * pulseScale;

        // Pulse Down
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / halfTime;
            dialogueBox.transform.localScale = Vector3.Lerp(start, target, t);
            yield return null;
        }

        // Load Next Sentence while box is squashed
        AdvanceLine();

        // Pulse Up
        t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / halfTime;
            dialogueBox.transform.localScale = Vector3.Lerp(target, start, t);
            yield return null;
        }
        dialogueBox.transform.localScale = start;
    }

    IEnumerator AnimateBox(Vector3 targetScale, float duration, AnimationCurve curve, System.Action onComplete = null)
    {
        Vector3 startScale = dialogueBox.transform.localScale;
        float t = 0;
        while (t < duration)
        {
            t += Time.deltaTime;
            float curveVal = curve.Evaluate(t / duration);
            dialogueBox.transform.localScale = Vector3.LerpUnclamped(startScale, targetScale, curveVal);
            yield return null;
        }
        dialogueBox.transform.localScale = targetScale;
        onComplete?.Invoke();
    }

    private void CloseDialogue()
    {
        isTyping = false;
        if (choicePanel) choicePanel.SetActive(false);
        StartCoroutine(AnimateBox(Vector3.zero, 0.2f, AnimationCurve.Linear(0, 0, 1, 1), () =>
        {
            isDialogueActive = false;
            dialogueBox.SetActive(false);
            if (specialUI) specialUI.SetActive(false);
        }));
    }

    private void ShowChoiceMenu()
    {
        isChoosing = true;
        choicePanel.SetActive(true);
        UpdateArrowPosition();
    }

    private void HandleChoiceInput()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.S))
        {
            selectedOption = 1 - selectedOption;
            UpdateArrowPosition();
        }
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return))
        {
            CloseDialogue();
            if (selectedOption == 0 && specialUI) specialUI.SetActive(true);
        }
    }

    private void UpdateArrowPosition()
    {
        if (arrow && yesOption && noOption)
            arrow.position = selectedOption == 0 ? yesOption.position : noOption.position;
    }

    // --- PUBLIC METHODS ---

    public bool IsDialogueActive()
    {
        return isDialogueActive || isChoosing;
    }

    public void ForceCloseAllUI()
    {
        StopAllCoroutines();
        isDialogueActive = false;
        isTyping = false;
        isChoosing = false;

        if (dialogueBox)
        {
            dialogueBox.SetActive(false);
            dialogueBox.transform.localScale = Vector3.zero;
        }
        if (choicePanel) choicePanel.SetActive(false);
        if (specialUI) specialUI.SetActive(false);
    }
}