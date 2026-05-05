using UnityEngine;

public class GrassPlayerBroadcaster : MonoBehaviour
{
    [SerializeField] private Transform player;
    private static readonly int PlayerPosID = Shader.PropertyToID("_PlayerPos");

    void LateUpdate()
    {
        if (player == null) return;
        Shader.SetGlobalVector(PlayerPosID, player.position);
    }
}