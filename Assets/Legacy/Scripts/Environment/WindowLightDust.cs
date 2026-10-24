using UnityEngine;

[ExecuteAlways]
public sealed class WindowLightDust : MonoBehaviour
{
    [Header("Dust")]
    [SerializeField] private Material _material;
    [SerializeField] private Vector3 _volume = new Vector3(8f, 6f, 10f);
    [SerializeField, Min(0f)] private float _emissionRate = 6f;
    private GameObject _dust;

    void OnEnable()
    {
        _dust = new GameObject("FloatingDust") { hideFlags = HideFlags.DontSave };
        _dust.transform.SetParent(transform, false);
        ParticleSystem particles = _dust.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.loop = true; main.prewarm = true; main.playOnAwake = false; main.duration = 16f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.015f, 0.055f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f); main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        ParticleSystem.EmissionModule emission = particles.emission; emission.rateOverTime = _emissionRate;
        ParticleSystem.ShapeModule shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = _volume;
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true; velocity.x = 0.012f; velocity.y = -0.018f; velocity.z = -0.025f;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = _material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particles.Simulate(16f, true, true); particles.Play();
    }

    void OnDisable()
    {
        if (Application.isPlaying) Destroy(_dust);
        else DestroyImmediate(_dust);
    }
}
