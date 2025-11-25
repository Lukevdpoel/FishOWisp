using UnityEngine;
using UnityEngine.EventSystems;

public class PauseMenuDebugger : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log("Click!!");
    }
}
