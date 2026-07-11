using System;
using UnityEngine;

[Serializable]
public class FishEncyclopediaEntry
{
    public FishPreset preset;

    public int hasCaught;
    public float largestCaught;
    public float smallestCaught;

    // How many distinct fish of this species the player has zoom-researched (right-mouse aim lock).
    // Drives the research checklist's "Zoom in on fish 10×/20×" items alongside hasCaught (catches).
    public int zoomResearchCount;

    public void RegisterZoom() => zoomResearchCount++;

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
