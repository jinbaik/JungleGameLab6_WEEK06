using UnityEngine;

public enum PlateEffect
{
    [InspectorName("None")] None = 0,
    [InspectorName("Underwater")] Reverb = 1,
    [InspectorName("High Pitch")] Pitch = 2,
    [InspectorName("Echo")] Delay = 3,
}
