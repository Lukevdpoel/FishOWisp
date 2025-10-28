using System;
using UnityEngine;

[Serializable]
public class FishEncyclopediaEntry
{
    public FishPreset preset;

    public int hasCaught;
    public float largestCaught;
    public float smallestCaught;

    public void RegisterCatch(float length)
    {
        if (hasCaught == 0)
        {
            hasCaught = 1;
            smallestCaught = largestCaught = length;
        }
        else
        {
            hasCaught++;
            if (length > largestCaught) largestCaught = length;
            if (length < smallestCaught) smallestCaught = length;
        }
    }
}
