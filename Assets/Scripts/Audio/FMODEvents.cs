using UnityEngine;
using FMODUnity;

public class FMODEvents : MonoBehaviour
{
    [field: Header("Keyboard SFX")]
    [field: SerializeField] public EventReference BlueSwitch { get; private set; }
    [field: SerializeField] public EventReference BrownSwitch { get; private set; }
    [field: SerializeField] public EventReference RedSwitch { get; private set; }
    [field: SerializeField] public EventReference SansSwitch { get; private set; }

    [field: SerializeField] public EventReference PopSwitch { get; private set; }
    [field: SerializeField] public EventReference TypewriterSwitch { get; private set; }
    [field: SerializeField] public EventReference BreakKeyCap { get; private set; }


    public static FMODEvents instance { get; private set; }
    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogError("Found more than one FMOD Events instance in the scene.");
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }


}
