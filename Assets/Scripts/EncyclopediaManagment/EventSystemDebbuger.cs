using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class EventSystemDebugger : MonoBehaviour
{
    void Update()
    {
        // Check if mouse is clicked
        if (Input.GetMouseButtonDown(0))
        {
            // Create a pointer event for the mouse position
            PointerEventData pointerData = new PointerEventData(EventSystem.current);
            pointerData.position = Input.mousePosition;

            // Raycast using the EventSystem
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            Debug.Log($"<color=yellow>Click detected!</color> Found {results.Count} UI objects under mouse.");

            foreach (RaycastResult result in results)
            {
                Debug.Log($"- Hit: <b>{result.gameObject.name}</b> (Depth: {result.depth}, SortingLayer: {result.sortingLayer})");
            }

            if (results.Count == 0)
            {
                Debug.Log("<color=red>No UI elements hit.</color> Check if your Canvas has a GraphicRaycaster.");
            }
        }
    }
}