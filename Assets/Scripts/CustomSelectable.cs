using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CustomSelectable : Selectable, ISelectHandler,IDeselectHandler
{
    List<CustomUIElement> customUIElements = new List<CustomUIElement>();
    protected override void Awake()
    {
        base.Awake();
        customUIElements = GetComponentsInChildren<CustomUIElement>().ToList();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        foreach (var customElement in customUIElements)
        {
            customElement.OnDeselect(eventData);
        }
    }
    public void OnSelect(BaseEventData eventData)
    {
        foreach (var customElement in customUIElements)
        {
            customElement.OnSelect(eventData);
        }
    }
}
