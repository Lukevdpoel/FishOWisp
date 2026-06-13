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
#if UNITY_EDITOR
            Debug.Log("[CustomButton] Pointer hovering: " + gameObject.name);
            Debug.Log($"[CustomButton] Invoking onHover on {gameObject.name}");
#endif
            onHover?.Invoke();
        }
    }
    
    private void HandleControllerInput()
    {
        bool submitPressed = Input.GetButton("Submit");
        
        if (submitPressed && !wasSubmitPressed && !isPressed)
        {
#if UNITY_EDITOR
            Debug.Log("[Custom Button] Controller button down: " + gameObject.name);
#endif
            isPressed = true;
#if UNITY_EDITOR
            Debug.Log($"[CustomButton] Invoking onPressed on {gameObject.name}");
#endif
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
#if UNITY_EDITOR
        Debug.Log("[Custom Button] Pointer entered: " + gameObject.name);
#endif

        if (!isPressed)
        {
#if UNITY_EDITOR
            Debug.Log("[Custom Button] Pointer entered while not pressed: " + gameObject.name);
#endif
            /*if(onEnterSoundEffects.Count > 0)
                onEnterSoundEffects.GetRandom(sound => 
                {
                    if (sound != null)
                    {
                        TempAudioManager.Instance.PlayOneShot(sound, transform.position, 0.25f, 1, true, transform);
                    }
                });*/
#if UNITY_EDITOR
            Debug.Log($"[CustomButton] Invoking onEnter on {gameObject.name}");
#endif
            onEnter?.Invoke();
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[Custom Button] Pointer entered while pressed: " + gameObject.name);
#endif
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        hasHighlighted = false;
        if (!interactible)
            return;
        
#if UNITY_EDITOR
        Debug.Log("[Custom Button] Pointer exited: " + gameObject.name);
#endif
        if (!isPressed)
        {
#if UNITY_EDITOR
            Debug.Log("[Custom Button] Pointer exited while not pressed: " + gameObject.name);
            Debug.Log($"[CustomButton] Invoking onExit on {gameObject.name}");
#endif
            onExit?.Invoke();
        }
        else
        {
#if UNITY_EDITOR
            Debug.Log("[Custom Button] Pointer exited while pressed: " + gameObject.name);
#endif
        }
    }
    
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!interactible)
            return;
#if UNITY_EDITOR
        Debug.Log("[Custom Button] Pointer down: " + gameObject.name);
#endif
        isPressed = true;
#if UNITY_EDITOR
        Debug.Log($"[CustomButton] Invoking onPressed on {gameObject.name}");
#endif
        onPressed?.Invoke();
    }
    
    private async void HandleRelease()
    {
        if (!interactible)
            return;
        if (isPressed)
        {
#if UNITY_EDITOR
            Debug.Log("[Custom Button] Pointer up: " + gameObject.name);
#endif
            isPressed = false;

            if (isHovering)
            {
#if UNITY_EDITOR
                Debug.Log("[Custom Button] Pointer submitted - pointer up while hovered: " + gameObject.name);
                Debug.Log($"[CustomButton] Invoking onReleasedOverButton on {gameObject.name}");
#endif
                onReleasedOverButton?.Invoke();
#if UNITY_EDITOR
                Debug.Log($"[CustomButton] Invoking unDelayedSubmit on {gameObject.name}");
#endif
                unDelayedSubmit?.Invoke();
                await Task.Delay((int)(submitDelay * 1000));
#if UNITY_EDITOR
                Debug.Log($"[CustomButton] Invoking onSubmit on {gameObject.name}");
#endif
                onSubmit?.Invoke();
            }
            else
            {
#if UNITY_EDITOR
                Debug.Log("[Custom Button] Pointer up while not hovered: " + gameObject.name);
                Debug.Log($"[CustomButton] Invoking onReleasedOutsideButton on {gameObject.name}");
#endif
                onReleasedOutsideButton?.Invoke();
#if UNITY_EDITOR
                Debug.Log($"[CustomButton] Invoking onIdle on {gameObject.name}");
#endif
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
#if UNITY_EDITOR
        Debug.Log($"[CustomButton] Invoking onNotSelected on {gameObject.name}");
#endif
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