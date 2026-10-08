using UnityEngine;

public sealed class KeycapBreakParticles : MonoBehaviour
{
    [Header("Fragments")]
    [SerializeField] private Mesh[] _fragmentMeshes;
    [SerializeField] private Material _material;
    [SerializeField, Min(1)] private int _count = 24;
    [SerializeField, Min(0.1f)] private float _lifetime = 1.5f;

    void Start()
    {
        ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.loop = false; main.playOnAwake = false; main.duration = 0.1f; main.startLifetime = _lifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 16f); main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startRotation3D = true; main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); main.startRotationY = main.startRotationX; main.startRotationZ = main.startRotationX;
        main.gravityModifier = 0.15f; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        ParticleSystem.EmissionModule emission = particles.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)_count) });
        ParticleSystem.ShapeModule shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 60f; shape.radius = 0.2f;
        shape.position = new Vector3(0f, 0.26f, 0f); shape.rotation = new Vector3(-90f, 0f, 0f);
        ParticleSystem.RotationOverLifetimeModule spin = particles.rotationOverLifetime; spin.enabled = true; spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f); spin.y = spin.x; spin.z = spin.x;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.SetMeshes(_fragmentMeshes); renderer.sharedMaterial = _material;
        particles.Play(); Destroy(gameObject, _lifetime + 0.2f);
    }
}
