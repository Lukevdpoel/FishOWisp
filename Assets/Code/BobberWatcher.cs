using UnityEngine;

public class BobberWatcher : MonoBehaviour
{
    public string bobberTag = "Bobber"; // Tag your bobber prefab with this
    public GameObject objectToHide;     // The object to hide when bobber exists

    void Update()
    {
        GameObject bobber = GameObject.FindWithTag(bobberTag);

        if (bobber != null)
        {
            if (objectToHide.activeSelf)
                objectToHide.SetActive(false);
        }
        else
        {
            if (!objectToHide.activeSelf)
                objectToHide.SetActive(true);
        }
    }
}
