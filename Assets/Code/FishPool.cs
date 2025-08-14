using UnityEngine;

[CreateAssetMenu(menuName = "Fishing/Fish Pool")]
public class FishPool : ScriptableObject
{
    public string[] fishTypes;

    public string GetRandomFish()
    {
        if (fishTypes == null || fishTypes.Length == 0) return "Nothing";
        return fishTypes[Random.Range(0, fishTypes.Length)];
    }
}
