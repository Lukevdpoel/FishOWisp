using System.Collections.Generic;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CustomButton : CustomUIElement, IPointerEnterHandler, IPointerExitHandler, ISubmitHandler,
    IPointerDownHandler, ISelectHandler
{
    public bool interactible = true;
    [FoldoutGroup("Functional")]
    [SerializeField] public UnityEvent onIdle = new UnityEvent();
    [FoldoutGroup("Functional")]
    [SerializeField] public UnityEvent onHover = new UnityEvent();
    [FoldoutGroup("Functional")]
    [SerializeField] public UnityEvent unDelayedSubmit = new UnityEvent();
    [FoldoutGroup("Functional")]
    [SerializeField] public UnityEvent onSubmit = new UnityEvent();
    [FoldoutGroup("Functional")] [SerializeField]
    public float submitDelay = 0;
    
    [FoldoutGroup("ButtonStates", expanded: true)]
    [SerializeField] public UnityEvent onEnter = new UnityEvent();
    [FoldoutGroup("ButtonStates")]
    [SerializeField] public UnityEvent onExit = new UnityEvent();
    [FoldoutGroup("ButtonStates")]
    [SerializeField] public UnityEvent onPressed = new UnityEvent();
    [FoldoutGroup("ButtonStates")]
    [SerializeField] public UnityEvent onReleasedOverButton = new UnityEvent();
    [FoldoutGroup("ButtonStates")]
    [SerializeField] public UnityEvent onReleasedOutsideButton = new UnityEvent();
    [FoldoutGroup("ButtonStates")]
    [SerializeField] public UnityEvent onNotSelected = new UnityEvent();
    
    public List<AudioClip> onEnterSoundEffects = new List<AudioClip>();
    
    private bool isPressed = false;
    private bool isHovering = false;
    private bool hasHighlighted = false;
    private bool isSelected = false;
    private bool wasSubmitPressed = false;
    
    private void OnEnable()
    {
        isPressed = false;
        isHovering = false;
        hasHighlighted = false;
        isSelected = false;
        wasSubmitPressed = false;
    }
    
    private void Update()
    {
        if (!interactible)
            return;
        if (isSelected )
        {
            HandleControllerInput();
        }
        
        if (isPressed)
        {
            bool isReleased = Input.GetMouseButtonUp(0);
            
            if (isSelected)
            {
                bool submitReleased = !Input.GetButton("Submit") && wasSubmitPressed;
                isReleased = isReleased || submitReleased;
            }
            
            if (isReleased)
            {
                HandleRelease();
            }
        }

        if (isHovering)
        {
            if (!interactible)
                return;
            if (!hasHighlighted)
            {
                OnPointerEnter(null);
            }
            Debug.Log("[CustomButton] Pointer hovering: " + gameObject.name);
            Debug.Log($"[CustomButton] Invoking onHover on {gameObject.name}");
            onHover?.Invoke();
        }
    }
    
    private void HandleControllerInput()
    {
        bool submitPressed = Input.GetButton("Submit");
        
        if (submitPressed && !wasSubmitPressed && !isPressed)
        {
            Debug.Log("[Custom Button] Controller button down: " + gameObject.name);
            isPressed = true;
            Debug.Log($"[CustomButton] Invoking onPressed on {gameObject.name}");
            onPressed?.Invoke();
        }
        
        wasSubmitPressed = submitPressed;
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        
        if (!interactible)
            return;
        hasHighlighted = true;
        Debug.Log("[Custom Button] Pointer entered: " + gameObject.name);
        
        if (!isPressed)
        {
            Debug.Log("[Custom Button] Pointer entered while not pressed: " + gameObject.name);
            /*if(onEnterSoundEffects.Count > 0)
                onEnterSoundEffects.GetRandom(sound => 
                {
                    if (sound != null)
                    {
                        TempAudioManager.Instance.PlayOneShot(sound, transform.position, 0.25f, 1, true, transform);
                    }
                });*/
            Debug.Log($"[CustomButton] Invoking onEnter on {gameObject.name}");
            onEnter?.Invoke();
        }
        else
        {
            Debug.Log("[Custom Button] Pointer entered while pressed: " + gameObject.name);
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        hasHighlighted = false;
        if (!interactible)
            return;
        
        Debug.Log("[Custom Button] Pointer exited: " + gameObject.name);
        if (!isPressed)
        {
            Debug.Log("[Custom Button] Pointer exited while not pressed: " + gameObject.name);
            Debug.Log($"[CustomButton] Invoking onExit on {gameObject.name}");
            onExit?.Invoke();
        }
        else
        {
            Debug.Log("[Custom Button] Pointer exited while pressed: " + gameObject.name);
        }
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!interactible)
            return;
        Debug.Log("[Custom Button] Pointer down: " + gameObject.name);
        isPressed = true;
        Debug.Log($"[CustomButton] Invoking onPressed on {gameObject.name}");
        onPressed?.Invoke();
    }
    
    private async void HandleRelease()
    {
        if (!interactible)
            return;
        if (isPressed)
        {
            Debug.Log("[Custom Button] Pointer up: " + gameObject.name);
            isPressed = false;
            
            if (isHovering)
            {
                Debug.Log("[Custom Button] Pointer submitted - pointer up while hovered: " + gameObject.name);
                Debug.Log($"[CustomButton] Invoking onReleasedOverButton on {gameObject.name}");
                onReleasedOverButton?.Invoke();
                Debug.Log($"[CustomButton] Invoking unDelayedSubmit on {gameObject.name}");
                unDelayedSubmit?.Invoke();
                await Task.Delay((int)(submitDelay * 1000));
                Debug.Log($"[CustomButton] Invoking onSubmit on {gameObject.name}");
                onSubmit?.Invoke();
            }
            else
            {
                Debug.Log("[Custom Button] Pointer up while not hovered: " + gameObject.name);
                Debug.Log($"[CustomButton] Invoking onReleasedOutsideButton on {gameObject.name}");
                onReleasedOutsideButton?.Invoke();
                Debug.Log($"[CustomButton] Invoking onIdle on {gameObject.name}");
                onIdle?.Invoke();
            }
        }
    }
    
    public void ResetButton()
    {
        hasHighlighted = false;
        isPressed = false;
        isHovering = false;
        isSelected = false;
        wasSubmitPressed = false;
    }
    
    public bool IsPressed()
    {
        return isPressed;
    }
    
    private void OnDisable()
    {
        if (isPressed)
        {
            HandleRelease();
        }
        ResetButton();
    }

    public override void OnSelect(BaseEventData eventData)
    {
        isSelected = true;
        OnPointerEnter(null);
    }

    public override void OnDeselect(BaseEventData eventData)
    {
        isSelected = false;
        wasSubmitPressed = false;
        OnPointerExit(null);
    }
    
    public void OnSubmit(BaseEventData eventData)
    {
    }
    
    public void TriggerNotSelected()
    {
        Debug.Log($"[CustomButton] Invoking onNotSelected on {gameObject.name}");
        onNotSelected?.Invoke();
    }
    
    public void EnableInteractible()
    {
        interactible = true;
    }
    
    public void DisableInteractible()
    {
        interactible = false;
    }
}