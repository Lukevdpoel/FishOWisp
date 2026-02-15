using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static event System.Action<bool> OnDialogueStateChange;

    [Header("Hierarchy Setup")]
    public GameObject dialogueBoxPanel;
    public RectTransform backgroundRect;
    public TMP_Text nameText;
    public TMP_Text dialogueText;

    [Tooltip("Drag your Main Canvas here. Required for correct positioning.")]
    public Canvas mainCanvas;

    [Header("Positioning Settings")]
    [Tooltip("Distance from the NPC center in Canvas Units.")]
    public float horizontalPadding = 250f;
    [Tooltip("Height adjustment above the NPC.")]
    public float verticalPadding = 0f;
    public float repositionSpeed = 10f;

    [Tooltip("Prevents the box from touching screen edges.")]
    public float screenEdgeBuffer = 50f;

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

    // Internal State
    private Dialogue currentDialogue;
    private Transform currentSpeaker;
    private int currentLineIndex = 0;
    private bool isDialogueActive = false;
    private bool isTyping = false;
    private bool skipTyping = false;
    private bool isChoosing = false;
    private int selectedOption = 0;

    private RectTransform textRect;
    private RectTransform boxRect;
    private RectTransform canvasRect; // Cache the canvas rect
    private float currentScrollY = 0;
    private int linesScrolled = 0;
    private Vector2 targetAnchoredPos;

    public static DialogueManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (dialogueText) textRect = dialogueText.GetComponent<RectTransform>();
        if (dialogueBoxPanel) boxRect = dialogueBoxPanel.GetComponent<RectTransform>();

        // Auto-find canvas if not assigned
        if (mainCanvas == null) mainCanvas = GetComponentInParent<Canvas>();
        if (mainCanvas != null) canvasRect = mainCanvas.GetComponent<RectTransform>();

        ForceCloseAllUI();
    }

    void LateUpdate()
    {
        // Smoothly follow the target position
        if (isDialogueActive && currentSpeaker != null && Camera.main != null)
        {
            CalculateTargetPosition();
            boxRect.anchoredPosition = Vector2.Lerp(boxRect.anchoredPosition, targetAnchoredPos, Time.deltaTime * repositionSpeed);
        }
    }

    void Update()
    {
        if (isDialogueActive)
        {
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0))
            {
                if (isTyping) { skipTyping = true; return; }
                if (!isTyping) StartCoroutine(PulseAndContinue());
            }
        }

        if (isChoosing) HandleChoiceInput();
    }

    public void StartDialogue(Dialogue dialogue, Transform speaker)
    {
        currentDialogue = dialogue;
        currentSpeaker = speaker;
        currentLineIndex = 0;
        isDialogueActive = true;
        isChoosing = false;

        dialogueText.text = "";
        currentScrollY = 0;
        linesScrolled = 0;
        if (textRect) textRect.anchoredPosition = Vector2.zero;
        if (nameText) nameText.text = "";

        // Snap immediately to start position so it doesn't fly in from corner
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

    // 🟢 NEW: ROBUST COORDINATE CONVERSION
    private void CalculateTargetPosition()
    {
        // 1. Where is the NPC's Head in World Space?
        Vector3 npcHeadPos = currentSpeaker.position + Vector3.up * 2.0f;

        // 2. Convert World Space -> Screen Space (Pixel Coordinates)
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, npcHeadPos);

        // 3. Convert Screen Space -> Canvas Local Space (The coordinate system UI uses)
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main, out localPoint);

        // 4. Determine Side (Left or Right relative to Canvas Center)
        // If the NPC is on the right side of the screen, we push the box Left.
        // We compare the localPoint x against 0 (Canvas Center)
        bool isRightSide = localPoint.x > 0;

        float xOffset = isRightSide ? -horizontalPadding : horizontalPadding;

        // 5. Apply Offsets
        targetAnchoredPos = new Vector2(localPoint.x + xOffset, localPoint.y + verticalPadding);

        // 6. Clamp to Canvas Edges (Keep it on screen)
        // We use the canvasRect size to determine boundaries
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
        StopCoroutine("TypeText");
        StartCoroutine(TypeText(textToType));
    }

    IEnumerator TypeText(string text)
    {
        isTyping = true;
        skipTyping = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            dialogueText.text += c;
            dialogueText.ForceMeshUpdate();

            if (dialogueText.textInfo.lineCount > maxVisibleLines + linesScrolled)
            {
                linesScrolled++;
                yield return StartCoroutine(ScrollTextUp(dialogueText.textInfo.lineInfo[0].lineHeight));
            }

            if (typingAudio && !char.IsWhiteSpace(c)) typingAudio.Play();

            if (skipTyping)
            {
                if (i + 1 < text.Length)
                {
                    dialogueText.text += text.Substring(i + 1);
                    dialogueText.ForceMeshUpdate();
                }

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
        if (currentLineIndex < currentDialogue.lines.Length) DisplayLine();
        else { if (currentDialogue.isQuestion) ShowChoiceMenu(); else CloseDialogue(); }
    }

    IEnumerator PulseAndContinue()
    {
        float halfTime = pulseDuration / 2f;
        Vector3 start = Vector3.one;
        Vector3 target = start * pulseScale;
        float t = 0;
        while (t < 1f) { t += Time.deltaTime / halfTime; dialogueBoxPanel.transform.localScale = Vector3.Lerp(start, target, t); yield return null; }
        AdvanceLine();
        t = 0;
        while (t < 1f) { t += Time.deltaTime / halfTime; dialogueBoxPanel.transform.localScale = Vector3.Lerp(target, start, t); yield return null; }
        dialogueBoxPanel.transform.localScale = start;
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
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)) { selectedOption = 1 - selectedOption; UpdateArrowPosition(); }
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return)) { CloseDialogue(); if (selectedOption == 0 && specialUI) specialUI.SetActive(true); }
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