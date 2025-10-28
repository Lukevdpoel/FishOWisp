using UnityEngine;

// This is a simple static helper class. 
// Its only job is to hold a reference to the fish 
// that is currently being dragged, so the drop zone can identify it.
public static class FishDragger
{
    public static PhysicalFish CurrentDraggedFish { get; set; }
}