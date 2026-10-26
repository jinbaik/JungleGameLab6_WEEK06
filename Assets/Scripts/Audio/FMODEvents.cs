using UnityEngine;
using FMODUnity;

public class FMODEvents : MonoBehaviour
{
    [field: Header("Ambient Sound")]
    [field: SerializeField] public EventReference IndoorAmbient { get; private set; }
    [field: Header("UI")]
    [field: SerializeField] public EventReference Button { get; private set; }
    [field: Header("Keyboard SFX")]
    [field: SerializeField] public EventReference BlueSwitch { get; private set; }
    [field: SerializeField] public EventReference BrownSwitch { get; private set; }
    [field: SerializeField] public EventReference RedSwitch { get; private set; }
    [field: SerializeField] public EventReference SansSwitch { get; private set; }

    [field: SerializeField] public EventReference PopSwitch { get; private set; }
    [field: SerializeField] public EventReference TypewriterSwitch { get; private set; }
    [field: SerializeField] public EventReference FireWorksSwitch { get; private set; }
    [field: SerializeField] public EventReference AnimalSwitch { get; private set; }
    [field: SerializeField] public EventReference BreakKeyCap { get; private set; }
    [field: SerializeField] public EventReference PutOnKey { get; private set; }
    [field: SerializeField] public EventReference RemoveKey { get; private set; }
    [field: Header("Other SFX")]
    [field: SerializeField] public EventReference FootSteps { get; private set; }
    [field: SerializeField] public EventReference Throwing { get; private set; }
    [field: SerializeField] public EventReference Trash { get; private set; }
    [field: SerializeField] public EventReference VendingMachineThud { get; private set; }
    [field: SerializeField] public EventReference ThumpKeyBoard { get; private set; }
    [field: SerializeField] public EventReference Email { get; private set; }
    [field: SerializeField] public EventReference DrawerOpen { get; private set; }
    [field: SerializeField] public EventReference DrawerClose { get; private set; }
    [field: Header("Radio SFX")]
    [field: SerializeField] public EventReference RadioChange { get; private set; }
    [field: SerializeField] public EventReference RadioTurnOnOff { get; private set; }
    [field: Header("Radio Music")]
    [field: SerializeField] public EventReference RadioMusic { get; private set; }


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
