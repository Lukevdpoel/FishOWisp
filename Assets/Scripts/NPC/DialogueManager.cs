using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : GenericSingleton<DialogueManager>
{
    // EVENT SYSTEM
    public static event System.Action<bool> OnDialogueStateChange;

    [Header("Hierarchy Setup")]
    public GameObject dialogueBoxPanel;
    public RectTransform backgroundRect;
    public TMP_Text nameText;
    public TMP_Text dialogueText;
    public Canvas mainCanvas;

    [Header("Continue Arrow")]
    [Tooltip("Drag the Arrow UI Image here")]
    public GameObject continueArrow;
    [Tooltip("How fast the arrow fades in and out")]
    public float arrowBlinkSpeed = 4f;
    public float arrowBlinkSharpness = 2f;

    [Header("Positioning Settings")]
    public float horizontalPadding = 0f;
    public float verticalPadding = 100f;
    public float repositionSpeed = 10f;
    public float screenEdgeBuffer = 50f;

    [Header("Scroll Settings")]
    public int maxVisibleLines = 3;
    public float scrollSpeed = 0.2f;

    [Header("Animation")]
    public float openDuration = 0.3f;
    public AnimationCurve openCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float pulseDuration = 0.15f;
    public float pulseScale = 0.92f;

    [Header("Typewriter & Audio")]
    public float typingSpeed = 0.03f;
    [Tooltip("The Audio Source component attached to this Manager")]
    public AudioSource audioSource;
    [Tooltip("List of sounds to pick from at the start of a sentence")]
    public AudioClip[] sentenceStartSounds;

    [Header("Choice UI")]
    public GameObject choicePanel;
    public RectTransform arrow;
    public RectTransform yesOption;
    public RectTransform noOption;
    public GameObject specialUI;

    // Internal State
    private Dialogue currentDialogue;
    public Transform currentSpeaker;
    private int currentLineIndex = 0;
    private bool isDialogueActive = false;
    private bool isTyping = false;
    private bool skipTyping = false;
    private bool isChoosing = false;
    private int selectedOption = 0;

    private RectTransform textRect;
    private RectTransform boxRect;
    private RectTransform canvasRect;
    private float currentScrollY = 0;
    private int linesScrolled = 0;
    private Vector2 targetAnchoredPos;
    private WaitForSeconds typingWait;

    // Arrow Internal State
    private CanvasGroup arrowCanvasGroup;
    private Vector3 arrowBaseScale = Vector3.one;

    private Coroutine typeTextRoutine;

    protected override void Awake()
    {
        base.Awake();

        if (dialogueText) textRect = dialogueText.GetComponent<RectTransform>();
        if (dialogueBoxPanel) boxRect = dialogueBoxPanel.GetComponent<RectTransform>();

        if (mainCanvas == null) mainCanvas = GetComponentInParent<Canvas>();
        if (mainCanvas != null) canvasRect = mainCanvas.GetComponent<RectTransform>();

        // Setup Arrow Component
        if (continueArrow != null)
        {
            arrowBaseScale = continueArrow.transform.localScale;
            arrowCanvasGroup = continueArrow.GetComponent<CanvasGroup>();
            if (arrowCanvasGroup == null) arrowCanvasGroup = continueArrow.AddComponent<CanvasGroup>();
        }

        typingWait = new WaitForSeconds(typingSpeed);
        ForceCloseAllUI();
    }

    void LateUpdate()
    {
        if (isDialogueActive && currentSpeaker != null && Camera.main != null)
        {
            CalculateTargetPosition();
            boxRect.anchoredPosition = Vector2.Lerp(boxRect.anchoredPosition, targetAnchoredPos, Time.deltaTime * repositionSpeed);
        }
    }

    void Update()
    {
        // ARROW BLINK LOGIC
        if (continueArrow != null)
        {
            bool shouldShowArrow = isDialogueActive && !isTyping && !isChoosing;
            continueArrow.SetActive(shouldShowArrow);

            if (shouldShowArrow && arrowCanvasGroup != null)
            {
                // Sharp fading math (invisible half the time, pops up quickly)
                float wave = Mathf.Sin(Time.unscaledTime * arrowBlinkSpeed);
                arrowCanvasGroup.alpha = Mathf.Clamp01(wave * arrowBlinkSharpness);
            }
        }

        // Input
        if (isDialogueActive)
        {
            // A advances/confirms; B also skips through dialogue (it doubles as the cancel/close
            // verb everywhere else, but here it just keeps the lines moving).
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0)
                || GamepadInput.InteractPressed || GamepadInput.ConfirmPressed || GamepadInput.CancelPressed)
            {
                if (isTyping) { skipTyping = true; return; }
                if (!isTyping) StartCoroutine(PulseAndContinue());
            }
        }

        if (isChoosing) HandleChoiceInput();
    }

    public void StartDialogue(Dialogue dialogue, Transform speaker)
    {
        // Guard against an empty/unassigned dialogue — DisplayLine indexes lines[0] blind, so a
        // Dialogue with no lines (e.g. a freshly created asset, or a signpost with no pages) would
        // otherwise throw IndexOutOfRange from the open-animation callback.
        if (dialogue == null || dialogue.lines == null || dialogue.lines.Length == 0)
        {
            Debug.LogWarning("DialogueManager.StartDialogue: dialogue has no lines — ignoring.", speaker);
            return;
        }

        currentDialogue = dialogue;
        currentSpeaker = speaker;
        currentLineIndex = 0;
        isDialogueActive = true;
        isChoosing = false;

        dialogueText.text = "";
        dialogueText.maxVisibleCharacters = 0; // Prevent flash of text
        currentScrollY = 0;
        linesScrolled = 0;

        if (textRect) textRect.anchoredPosition = Vector2.zero;
        if (nameText) nameText.text = "";

        if (currentSpeaker != null && Camera.main != null)
        {
            CalculateTargetPosition();
            boxRect.anchoredPosition = targetAnchoredPos;
        }

        dialogueBoxPanel.SetActive(true);
        dialogueBoxPanel.transform.localScale = Vector3.zero;

        OnDialogueStateChange?.Invoke(true);

        StartCoroutine(AnimateBox(Vector3.one, openDuration, openCurve, () =>
        {
            DisplayLine();
        }));
    }

    private void CalculateTargetPosition()
    {
        Vector3 npcHeadPos = currentSpeaker.position + Vector3.up * 2.0f;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, npcHeadPos);

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main, out localPoint);

        targetAnchoredPos = new Vector2(localPoint.x + horizontalPadding, localPoint.y + verticalPadding);

        float minX = (canvasRect.rect.width / -2f) + (boxRect.rect.width / 2f) + screenEdgeBuffer;
        float maxX = (canvasRect.rect.width / 2f) - (boxRect.rect.width / 2f) - screenEdgeBuffer;

        float minY = (canvasRect.rect.height / -2f) + (boxRect.rect.height / 2f) + screenEdgeBuffer;
        float maxY = (canvasRect.rect.height / 2f) - (boxRect.rect.height / 2f) - screenEdgeBuffer;

        targetAnchoredPos.x = Mathf.Clamp(targetAnchoredPos.x, minX, maxX);
        targetAnchoredPos.y = Mathf.Clamp(targetAnchoredPos.y, minY, maxY);
    }

    private void DisplayLine()
    {
        DialogueLine line = currentDialogue.lines[currentLineIndex];
        if (nameText) nameText.text = line.characterName;
        string textToType = (currentLineIndex == 0) ? line.text : "\n" + line.text;

        if (typeTextRoutine != null) StopCoroutine(typeTextRoutine);
        typeTextRoutine = StartCoroutine(TypeText(textToType));
    }

    // TYPEWRITER: Pre-loads text and reveals it, playing one sound at the start
    IEnumerator TypeText(string newTextChunk)
    {
        isTyping = true;
        skipTyping = false;

        // 🟢 NEW: Play a random sentence start sound
        if (audioSource != null && sentenceStartSounds != null && sentenceStartSounds.Length > 0)
        {
            // Pick a random clip from the array
            AudioClip randomClip = sentenceStartSounds[Random.Range(0, sentenceStartSounds.Length)];

            // Play it once, allowing it to overlap with previously playing sounds if needed
            audioSource.PlayOneShot(randomClip);
        }

        int startCharIndex = dialogueText.maxVisibleCharacters;

        dialogueText.text += newTextChunk;
        dialogueText.ForceMeshUpdate();

        int totalVisibleChars = dialogueText.textInfo.characterCount;

        for (int i = startCharIndex; i < totalVisibleChars; i++)
        {
            dialogueText.maxVisibleCharacters = i + 1;

            int currentLineOfChar = dialogueText.textInfo.characterInfo[i].lineNumber;

            if (currentLineOfChar >= maxVisibleLines + linesScrolled)
            {
                linesScrolled++;
                yield return StartCoroutine(ScrollTextUp(dialogueText.textInfo.lineInfo[0].lineHeight));
            }

            // Notice: The per-letter audio logic was completely removed from here!

            if (skipTyping)
            {
                dialogueText.maxVisibleCharacters = totalVisibleChars;

                int totalLines = dialogueText.textInfo.lineCount;
                if (totalLines > maxVisibleLines)
                {
                    float lineHeight = dialogueText.textInfo.lineInfo[0].lineHeight;
                    int linesToHide = totalLines - maxVisibleLines;
                    float targetY = linesToHide * lineHeight;
                    textRect.anchoredPosition = new Vector2(textRect.anchoredPosition.x, targetY);
                    currentScrollY = targetY;
                    linesScrolled = linesToHide;
                }
                break;
            }

            yield return typingWait;
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
        if (currentLineIndex < currentDialogue.lines.Length) DisplayLine();
        else { if (currentDialogue.isQuestion) ShowChoiceMenu(); else CloseDialogue(); }
    }

    IEnumerator PulseAndContinue()
    {
        float halfTime = pulseDuration / 2f;
        Vector3 boxStart = Vector3.one;
        Vector3 boxTarget = boxStart * pulseScale;
        Vector3 arrowTarget = arrowBaseScale * 0.5f;

        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / halfTime;
            dialogueBoxPanel.transform.localScale = Vector3.Lerp(boxStart, boxTarget, t);
            if (continueArrow) continueArrow.transform.localScale = Vector3.Lerp(arrowBaseScale, arrowTarget, t);
            yield return null;
        }

        AdvanceLine();

        t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime / halfTime;
            dialogueBoxPanel.transform.localScale = Vector3.Lerp(boxTarget, boxStart, t);
            if (continueArrow) continueArrow.transform.localScale = Vector3.Lerp(arrowTarget, arrowBaseScale, t);
            yield return null;
        }

        dialogueBoxPanel.transform.localScale = boxStart;
        if (continueArrow) continueArrow.transform.localScale = arrowBaseScale;
    }

    IEnumerator AnimateBox(Vector3 targetScale, float duration, AnimationCurve curve, System.Action onComplete = null)
    {
        Vector3 startScale = dialogueBoxPanel.transform.localScale;
        float t = 0;
        while (t < duration)
        {
            t += Time.deltaTime;
            float curveVal = curve.Evaluate(t / duration);
            dialogueBoxPanel.transform.localScale = Vector3.LerpUnclamped(startScale, targetScale, curveVal);
            yield return null;
        }
        dialogueBoxPanel.transform.localScale = targetScale;
        onComplete?.Invoke();
    }

    private void CloseDialogue()
    {
        isTyping = false;
        if (choicePanel) choicePanel.SetActive(false);
        StartCoroutine(AnimateBox(Vector3.zero, 0.2f, AnimationCurve.Linear(0, 0, 1, 1), () =>
        {
            isDialogueActive = false;
            dialogueBoxPanel.SetActive(false);
            currentSpeaker = null;
            if (specialUI) specialUI.SetActive(false);
            OnDialogueStateChange?.Invoke(false);
        }));
    }

    private void ShowChoiceMenu() { isChoosing = true; choicePanel.SetActive(true); UpdateArrowPosition(); }

    private void HandleChoiceInput()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
            || GamepadInput.DpadUpPressed || GamepadInput.DpadDownPressed) { selectedOption = 1 - selectedOption; UpdateArrowPosition(); }
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return)
            || GamepadInput.InteractPressed || GamepadInput.ConfirmPressed) { CloseDialogue(); if (selectedOption == 0 && specialUI) specialUI.SetActive(true); }
    }

    private void UpdateArrowPosition() { if (arrow && yesOption && noOption) arrow.position = selectedOption == 0 ? yesOption.position : noOption.position; }

    public bool IsDialogueActive() { return isDialogueActive || isChoosing; }

    public void ForceCloseAllUI()
    {
        StopAllCoroutines();
        isDialogueActive = false;
        isTyping = false;
        isChoosing = false;
        currentSpeaker = null;
        if (dialogueBoxPanel) { dialogueBoxPanel.SetActive(false); dialogueBoxPanel.transform.localScale = Vector3.zero; }
        if (choicePanel) choicePanel.SetActive(false);
        if (specialUI) specialUI.SetActive(false);
        OnDialogueStateChange?.Invoke(false);
    }
}