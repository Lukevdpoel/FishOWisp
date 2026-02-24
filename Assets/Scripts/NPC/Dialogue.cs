using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    public string characterName;
    [TextArea(2, 5)]
    public string text;
}

[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue")]
public class Dialogue : ScriptableObject
{
    public DialogueLine[] lines;

    [Header("End Settings")]
    [Tooltip("If true, the Yes/No box appears after the last line.")]
    public bool isQuestion = false;
}