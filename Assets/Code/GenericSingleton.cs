using System;
using Sirenix.OdinInspector;
using UnityEngine;

public class GenericSingleton<T> : SerializedMonoBehaviour
    where T : Component
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = GameObject.FindFirstObjectByType<T>();
            if (_instance == null)
            {
                GameObject obj = new GameObject(nameof(T));
                obj.AddComponent<T>();
                _instance = obj.GetComponent<T>();
            }
            return _instance;
        }
    }

    public virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }
}