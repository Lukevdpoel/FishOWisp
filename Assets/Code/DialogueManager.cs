using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    [Header("Dialogue UI")]
    public GameObject dialogueBox;
    public TMP_Text nameText;
    public TMP_Text dialogueText;

    [Header("Typewriter")]
    public float typingSpeed = 0.02f;
    public AudioSource typingAudio;

    [Header("Choice UI")]
    public GameObject choicePanel;
    public RectTransform arrow;
    public RectTransform yesOption;
    public RectTransform noOption;

    [Header("Optional UI to Open on YES")]
    public GameObject specialUI;

    private Dialogue currentDialogue;
    private int currentLineIndex = 0;
    private bool isDialogueActive = false;
    private bool isTyping = false;

    private int selectedOption = 0;
    private bool isChoosing = false;

    public static DialogueManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        if (isChoosing)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                selectedOption = 1 - selectedOption;
                UpdateArrowPosition();
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                ConfirmChoice();
            }

            return;
        }

        if (isDialogueActive && Input.GetKeyDown(KeyCode.E))
        {
            if (isTyping)
            {
                StopAllCoroutines();
                dialogueText.text = currentDialogue.lines[currentLineIndex].text;
                isTyping = false;
            }
            else
            {
                DisplayNextLine();
            }
        }
    }

    public void StartDialogue(Dialogue dialogue)
    {
        currentDialogue = dialogue;
        currentLineIndex = 0;
        isDialogueActive = true;

        dialogueBox.SetActive(true);
        DisplayLine();
    }

    private void DisplayLine()
    {
        DialogueLine line = currentDialogue.lines[currentLineIndex];
        nameText.text = line.characterName;
        StartCoroutine(TypeText(line.text));
    }

    private void DisplayNextLine()
    {
        currentLineIndex++;
        if (currentLineIndex < currentDialogue.lines.Length)
        {
            DisplayLine();
        }
        else
        {
            EndDialogue();
        }
    }

    IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = "";
        foreach (char c in text)
        {
            dialogueText.text += c;
            if (typingAudio) typingAudio.Play();
            yield return new WaitForSeconds(typingSpeed);
        }
        isTyping = false;
    }

    private void EndDialogue()
    {
        choicePanel.SetActive(false);
        specialUI.SetActive(false);
        isDialogueActive = false;
        dialogueBox.SetActive(false);
        ShowChoiceMenu();
    }

    private void ShowChoiceMenu()
    {
        isChoosing = true;
        selectedOption = 0;
        choicePanel.SetActive(true);
        UpdateArrowPosition();
    }

    private void UpdateArrowPosition()
    {
        arrow.position = selectedOption == 0 ? yesOption.position : noOption.position;
    }

    private void ConfirmChoice()
    {
        isChoosing = false;
        choicePanel.SetActive(false);

        if (selectedOption == 0)
        {
            if (specialUI != null)
                specialUI.SetActive(true);
        }
        else
        {
            // Do nothing
        }
    }

    public void ForceEndDialogue()
    {
        StopAllCoroutines();
        isTyping = false;
        isDialogueActive = false;
        dialogueBox.SetActive(false);
        isChoosing = false;
        choicePanel.SetActive(false);
    }

    public bool IsDialogueActive()
    {
        return isDialogueActive || isChoosing;
    }

    public void OnPlayerExitZone()
    {
        ForceEndDialogue();

        if (specialUI != null)
        {
            Debug.Log("Closing special UI"); // ✅ Debug check
            specialUI.SetActive(false);
        }
    }

    public void ForceCloseAllUI()
    {
        StopAllCoroutines();

        isTyping = false;
        isDialogueActive = false;
        isChoosing = false; // 🔥 CRUCIAL!

        if (dialogueBox != null) dialogueBox.SetActive(false);
        if (choicePanel != null) choicePanel.SetActive(false);
        if (specialUI != null) specialUI.SetActive(false);

        Debug.Log("Closed all UI");
    }


    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            DialogueManager.Instance.ForceCloseAllUI();
        }
    }


}
