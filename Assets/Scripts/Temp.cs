using Unity.Cinemachine;
using UnityEngine;

public class Temp : MonoBehaviour
{
    [SerializeField] private CinemachineCamera _fixedCam;

    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            OnExitSmashMode();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Keyboard"))
        {
            OnEnterSmashMode();
        }
    }

    private void OnEnterSmashMode()
    {
        _fixedCam.Priority = 20;
    }

    private void OnExitSmashMode()
    {
        _fixedCam.Priority = 0;
    }
}
