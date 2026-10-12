using UnityEngine;

// The particle systems of one material's falling leaves. The falling system
// sits at the canopy's centre, emits from a box inside it, and tumbles each
// leaf down at the fall speed with the wind's drift and some noise on top.
// A leaf that reaches the ground plane dies there and, through a collision
// sub-emitter, leaves a resting leaf lying flat in its place, which shrinks
// away at the end of its time on the ground. Every particle is the same quad
// with one random tile of the leaf atlas (a texture sheet: VaM's Unity draws
// no mesh particles when given several meshes).
//
// UnityEngine and FoliageFXConfig only, no VaM type, so it runs outside VaM
// as well. Nothing here catches; a throw reaches the caller.
sealed class LeafEmitter
{
    // The falling system's cycle; it loops, so this only paces its emission.
    const float CycleSeconds = 10f;
    // The emission box: three quarters of the canopy across, half of it
    // high, raised a tenth of its height so leaves start among the leaves.
    const float BoxWidthShare = 0.75f;
    const float BoxHeightShare = 0.5f;
    const float BoxRaiseShare = 0.1f;
    // Tumbling, radians per second either way, on each axis.
    const float SpinX = 3f;
    const float SpinY = 2f;
    const float SpinZ = 3f;
    // A falling leaf shrinks away over the last tenth of its life when it
    // never reaches the ground (a canopy hanging over a drop).
    const float FallShrinkAt = 0.9f;
    // The drop is never taken as shorter than this, nor the fall speed as
    // slower, and a leaf lives a second past its expected landing.
    const float MinDropMetres = 0.5f;
    const float MinFallSpeed = 0.05f;
    const float LifetimeSpareSeconds = 1f;
    const float LifetimeMinShare = 0.8f;
    // Each leaf's size, drift and speed vary around the setting by these.
    const float SizeMinShare = 0.7f;
    const float SizeMaxShare = 1.3f;
    const float DriftMinShare = 0.6f;
    const float DriftMaxShare = 1.4f;
    const float FallMinShare = 0.7f;
    const float FallMaxShare = 1.3f;
    // How many leaves the systems keep room for: rate times life, with this
    // much headroom, plus a few.
    const float FallingHeadroom = 1.3f;
    const float RestingHeadroom = 1.5f;
    const int ParticlesSpare = 8;
    // The flutter: a base strength with the wind's on top, slow and smooth.
    const float NoiseBase = 0.35f;
    const float NoisePerWind = 0.8f;
    const float NoiseFrequency = 0.6f;
    const float NoiseScroll = 0.35f;
    // A resting leaf lives its time on the ground give or take this share;
    // a time of 0 switches the resting leaves off, but the lifetime itself
    // never goes below the floor.
    const float RestMinShare = 0.7f;
    const float RestMaxShare = 1.3f;
    const float MinRestSeconds = 0.1f;

    public ParticleSystem Falling;
    public ParticleSystem Resting;
    public Transform Ground;
    public Bounds Canopy;
    public float GroundY;
    // The leaf shapes this emitter shows; another extraction means another emitter.
    public LeafSprites Sprites;

    ParticleSystemRenderer fallingRenderer;
    ParticleSystemRenderer restingRenderer;
    bool started;

    // A child of `parent`, so it follows the tree, with the parent's scale
    // undone, so the settings are metres. Stopped until the first Apply.
    public static LeafEmitter Create(Transform parent, Bounds canopy, float groundY, LeafSprites sprites, Material material)
    {
        GameObject go = new GameObject("FoliageFX falling leaves");
        go.transform.SetParent(parent, false);
        go.transform.position = canopy.center;
        go.transform.rotation = Quaternion.identity;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        go.transform.localScale = new Vector3(SafeInverse(parentScale.x), SafeInverse(parentScale.y), SafeInverse(parentScale.z));

        GameObject groundGo = new GameObject("ground");
        groundGo.transform.SetParent(go.transform, false);
        // A little above the tree's base: a leaf lying in the floor's own plane
        // is drawn into it, and some floors are drawn above their collider,
        // so a leaf at the base would vanish where it lands.
        groundGo.transform.position = new Vector3(canopy.center.x, groundY + FoliageFXConfig.LeafRestLift, canopy.center.z);
        groundGo.transform.rotation = Quaternion.identity;

        GameObject restGo = new GameObject("resting");
        restGo.transform.SetParent(go.transform, false);

        LeafEmitter e = new LeafEmitter();
        e.Canopy = canopy;
        e.GroundY = groundY;
        e.Ground = groundGo.transform;
        e.Sprites = sprites;
        e.Falling = go.AddComponent<ParticleSystem>();
        e.Resting = restGo.AddComponent<ParticleSystem>();
        e.Falling.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = e.Falling.main;
        main.duration = CycleSeconds;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        // The resting leaf is born flat (a quarter turn about X lays the quad
        // on the ground), at a random heading, sized like a falling one (Apply).
        ParticleSystem.MainModule rest = e.Resting.main;
        rest.duration = 1f;
        rest.loop = false;
        rest.playOnAwake = false;
        rest.simulationSpace = ParticleSystemSimulationSpace.World;
        rest.scalingMode = ParticleSystemScalingMode.Shape;
        rest.startSpeed = 0f;
        rest.gravityModifier = 0f;
        rest.startRotation3D = true;
        rest.startRotationX = Mathf.PI * 0.5f;
        rest.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        rest.startRotationZ = 0f;
        ParticleSystem.EmissionModule restEmission = e.Resting.emission;
        restEmission.rateOverTime = 0f;
        restEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.ShapeModule restShape = e.Resting.shape;
        restShape.enabled = false;
        ParticleSystem.SizeOverLifetimeModule restSize = e.Resting.sizeOverLifetime;
        restSize.enabled = true;
        restSize.size = new ParticleSystem.MinMaxCurve(1f, ShrinkAtEnd(FoliageFXConfig.LeafRestShrinkAt));

        ParticleSystem.ShapeModule shape = e.Falling.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(canopy.size.x * BoxWidthShare, canopy.size.y * BoxHeightShare, canopy.size.z * BoxWidthShare);
        shape.position = new Vector3(0f, canopy.size.y * BoxRaiseShare, 0f);

        ParticleSystem.RotationOverLifetimeModule spin = e.Falling.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-SpinX, SpinX);
        spin.y = new ParticleSystem.MinMaxCurve(-SpinY, SpinY);
        spin.z = new ParticleSystem.MinMaxCurve(-SpinZ, SpinZ);

        ParticleSystem.SizeOverLifetimeModule size = e.Falling.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, ShrinkAtEnd(FallShrinkAt));

        // The ground plane stops a leaf dead: no bounce, its life ends, and
        // the small radius lets it land flat rather than hover at its size.
        ParticleSystem.CollisionModule collision = e.Falling.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.Planes;
        collision.SetPlane(0, e.Ground);
        collision.dampen = 1f;
        collision.bounce = 0f;
        collision.lifetimeLoss = 1f;
        collision.radiusScale = FoliageFXConfig.LeafCollisionRadius;

        ParticleSystem.SubEmittersModule sub = e.Falling.subEmitters;
        sub.enabled = true;
        // Nothing inherited: the ground ends the falling leaf's life, so its
        // size there is the end of its shrink, and a resting leaf inheriting
        // it was born at nothing.
        sub.AddSubEmitter(e.Resting, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);

        e.fallingRenderer = SetupRenderer(e.Falling, sprites, material);
        e.restingRenderer = SetupRenderer(e.Resting, sprites, material);
        SetupSheet(e.Falling, sprites);
        SetupSheet(e.Resting, sprites);
        return e;
    }

    // Everything a slider changes, safe to call every frame. `rate` is this
    // emitter's share of the tree's rate; `sizeM` the leaf's length in metres.
    public void Apply(float rate, float sizeM, float fallSpeed, float windStrength, float windDirectionDeg, float restSeconds, bool shadows)
    {
        if (Falling == null || Resting == null) return;
        float height = Mathf.Max(MinDropMetres, Canopy.max.y - GroundY);
        float lifetime = height / Mathf.Max(MinFallSpeed, fallSpeed) * FoliageFXConfig.LeafFallPathFactor + LifetimeSpareSeconds;
        Vector3 wind = Quaternion.Euler(0f, windDirectionDeg, 0f) * Vector3.forward * (windStrength * FoliageFXConfig.LeafWindDrift);
        float tileSize = sizeM * FoliageFXConfig.AtlasTileScale;

        ParticleSystem.MainModule main = Falling.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * LifetimeMinShare, lifetime);
        main.startSize = new ParticleSystem.MinMaxCurve(tileSize * SizeMinShare, tileSize * SizeMaxShare);
        main.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * lifetime * FallingHeadroom) + ParticlesSpare,
            FoliageFXConfig.LeafParticlesMin, FoliageFXConfig.LeafParticlesMax);

        ParticleSystem.EmissionModule emission = Falling.emission;
        emission.enabled = rate > 0f;
        emission.rateOverTime = rate;

        ParticleSystem.VelocityOverLifetimeModule velocity = Falling.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(wind.x * DriftMinShare, wind.x * DriftMaxShare);
        velocity.y = new ParticleSystem.MinMaxCurve(-fallSpeed * FallMaxShare, -fallSpeed * FallMinShare);
        velocity.z = new ParticleSystem.MinMaxCurve(wind.z * DriftMinShare, wind.z * DriftMaxShare);

        ParticleSystem.NoiseModule noise = Falling.noise;
        noise.enabled = true;
        noise.strength = NoiseBase + windStrength * NoisePerWind;
        noise.frequency = NoiseFrequency;
        noise.scrollSpeed = NoiseScroll;
        noise.damping = true;
        noise.octaveCount = 1;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        ParticleSystem.SubEmittersModule sub = Falling.subEmitters;
        sub.enabled = restSeconds > 0f;
        ParticleSystem.MainModule rest = Resting.main;
        rest.startSize = new ParticleSystem.MinMaxCurve(tileSize * SizeMinShare, tileSize * SizeMaxShare);
        float restTime = Mathf.Max(MinRestSeconds, restSeconds);
        rest.startLifetime = new ParticleSystem.MinMaxCurve(restTime * RestMinShare, restTime * RestMaxShare);
        rest.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * restTime * RestingHeadroom) + ParticlesSpare,
            FoliageFXConfig.LeafParticlesMin, FoliageFXConfig.LeafParticlesMax);

        UnityEngine.Rendering.ShadowCastingMode cast = shadows
            ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        if (fallingRenderer != null) fallingRenderer.shadowCastingMode = cast;
        if (restingRenderer != null) restingRenderer.shadowCastingMode = cast;

        if (!started)
        {
            started = true;
            Falling.Play(false);
        }
    }

    // The resting system and the ground are children of the falling one, so
    // one destroy takes all three. A CustomUnityAsset that clears its asset
    // destroys every child of its loader, this emitter among them; then there
    // is nothing left to destroy.
    public void Destroy()
    {
        if (Falling != null) LeafSprites.DestroyObject(Falling.gameObject);
        Falling = null;
        Resting = null;
        Ground = null;
        fallingRenderer = null;
        restingRenderer = null;
    }

    static ParticleSystemRenderer SetupRenderer(ParticleSystem ps, LeafSprites sprites, Material material)
    {
        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = sprites.Quad;
        r.sharedMaterial = material;
        r.alignment = ParticleSystemRenderSpace.World;
        r.receiveShadows = true;
        r.sortMode = ParticleSystemSortMode.None;
        return r;
    }

    // Each particle keeps one random leaf tile for its whole life.
    static void SetupSheet(ParticleSystem ps, LeafSprites sprites)
    {
        ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.mode = ParticleSystemAnimationMode.Grid;
        sheet.numTilesX = FoliageFXConfig.AtlasGrid;
        sheet.numTilesY = FoliageFXConfig.AtlasGrid;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, sprites.ShapeCount - 0.01f);
        sheet.cycleCount = 1;
    }

    static AnimationCurve ShrinkAtEnd(float from)
    {
        return new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(from, 1f), new Keyframe(1f, 0f));
    }

    static float SafeInverse(float v)
    {
        return Mathf.Abs(v) < 0.0001f ? 1f : 1f / v;
    }
}
