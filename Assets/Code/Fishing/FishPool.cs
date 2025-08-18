using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewFishPool", menuName = "Fishing/Fish Pool")]
public class FishPool : ScriptableObject
{
    public string poolName;
    [TextArea]
    public string description;

    [Tooltip("Fish that can be caught in this pool.")]
    public List<FishPreset> availableFish;

    [Header("Catch Timing")]
    public float minCatchTime = 2f;
    public float maxCatchTime = 5f; 
}