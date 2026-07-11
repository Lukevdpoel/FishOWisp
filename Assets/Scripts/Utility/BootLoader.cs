using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lives in the tiny "Boot" scene (build index 0). On launch it reads the last-active scene from
/// <see cref="SceneMemory"/> and loads it, so the game resumes where the player left off — and the
/// title-screen flythrough plays in THAT scene — instead of always starting in HUTBUILT.
///
/// The Boot scene carries no managers of its own; it just hands straight off to a real gameplay
/// scene, whose baked-in Managers prefab then spins everything up exactly as before. Because the
/// handoff happens before any heavy scene loads, we never pay to load HUTBUILT just to bounce away
/// from it.
/// </summary>
public class BootLoader : MonoBehaviour
{
    private void Start()
    {
        string target = SceneMemory.Load();
        Debug.Log($"[BootLoader] Resuming into last-active scene '{target}'.");
        SceneManager.LoadScene(target);
    }
}
