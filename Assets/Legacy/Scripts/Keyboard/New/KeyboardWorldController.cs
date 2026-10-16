using UnityEngine;

[RequireComponent(typeof(BoxCollider), typeof(BoxPickupController))]
public sealed class KeyboardWorldController : KeyboardInteractionController
{
    protected override void Awake() { base.Awake(); }
    protected override void Start() { base.Start(); }
    protected override void Update() { base.Update(); }
    protected override void FixedUpdate() { base.FixedUpdate(); }
    protected override void LateUpdate() { base.LateUpdate(); }
    protected override void OnTriggerStay(Collider other) { base.OnTriggerStay(other); }
    protected override void OnTriggerExit(Collider other) { base.OnTriggerExit(other); }
    protected override void OnEnable() { base.OnEnable(); }
    protected override void OnDisable() { base.OnDisable(); }
}