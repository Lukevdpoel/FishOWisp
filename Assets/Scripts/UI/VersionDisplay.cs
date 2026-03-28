using UnityEngine;
using TMPro;

[RequireComponent(typeof(TMP_Text))]
public class VersionDisplay : MonoBehaviour
{
    [SerializeField] private string prefix = "v";

    private void Start()
    {
        GetComponent<TMP_Text>().text = prefix + Application.version;
    }
}
