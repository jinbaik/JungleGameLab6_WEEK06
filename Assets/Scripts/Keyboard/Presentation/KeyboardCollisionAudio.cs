using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class KeyboardCollisionAudio : MonoBehaviour
{
    [Header("Collision Sound")]
    [SerializeField, Min(0f)] private float _minimumImpactSpeed = 2f;
    [SerializeField, Min(0f)] private float _minimumPlayInterval = 0.2f;
    [SerializeField] private LayerMask _surfaceLayers = 1;
    private Rigidbody _body;
    private double _lastPlayTime = double.NegativeInfinity;
    private double _lastPlayFixedTime = double.NegativeInfinity;

    void Awake()
    {
        _body = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!enabled || _body.isKinematic || collision.rigidbody != null ||
            (_surfaceLayers.value & (1 << collision.gameObject.layer)) == 0 ||
            Time.timeAsDouble - _lastPlayTime < _minimumPlayInterval ||
            _lastPlayFixedTime == Time.fixedTimeAsDouble)
            return;

        float strongestSpeed = 0f;
        Vector3 impactPosition = Vector3.zero;
        for (int index = 0; index < collision.contactCount; index++)
        {
            ContactPoint contact = collision.GetContact(index);
            if (contact.thisCollider.attachedRigidbody != _body ||
                contact.thisCollider.GetComponentInParent<KeycapHealth>() != null ||
                contact.thisCollider.GetComponentInParent<KeycapButton>() != null)
                continue;

            // 충돌 콜백이 제공하는 상대속도를 사용해 물리 반응 후의 감속 영향을 피한다.
            float normalSpeed = Mathf.Max(0f, Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (normalSpeed <= strongestSpeed) continue;

            strongestSpeed = normalSpeed;
            impactPosition = contact.point;
        }

        if (strongestSpeed <= 0f || strongestSpeed < _minimumImpactSpeed) return;

        AudioManager.instance.PlayOneShot(FMODEvents.instance.ThumpKeyBoard, impactPosition);
        _lastPlayTime = Time.timeAsDouble;
        _lastPlayFixedTime = Time.fixedTimeAsDouble;
    }
}
