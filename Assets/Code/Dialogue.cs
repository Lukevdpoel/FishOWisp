using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    public string characterName; // 🟢 THIS MUST EXIST
    [TextArea(2, 5)]
    public string text;
}

[CreateAssetMenu(fileName = "NewDialogue", menuName = "Dialogue")]
public class Dialogue : ScriptableObject
{
    public DialogueLine[] lines;
}
