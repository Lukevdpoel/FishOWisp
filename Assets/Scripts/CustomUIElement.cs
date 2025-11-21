using System;
using UnityEngine;
using UnityEngine.EventSystems;

public abstract class CustomUIElement : MonoBehaviour
{
    CustomSelectable _selectable;
    protected virtual void Awake()
    {
        if (_selectable == null)
        {
            _selectable = GetComponent<CustomSelectable>();
            if (_selectable == null)
            {
                _selectable = gameObject.AddComponent<CustomSelectable>();
            }
        }
       
    }

    public abstract void OnDeselect(BaseEventData eventData);
    public abstract void OnSelect(BaseEventData eventData);
}
