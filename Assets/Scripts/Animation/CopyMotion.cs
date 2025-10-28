using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CopyMotion : MonoBehaviour
{
    public bool mirror;
    public Transform targetLimb;
    ConfigurableJoint cj;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cj = GetComponent<ConfigurableJoint>();
    }

    // Update is called once per frame
    void Update()
    {
        if(!mirror)
        {
            cj.targetRotation = targetLimb.rotation;
        }
        else
        {
            cj.targetRotation = Quaternion.Inverse(targetLimb.rotation);
        }
    }
}
