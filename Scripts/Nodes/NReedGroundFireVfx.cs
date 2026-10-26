using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.TestSupport;

namespace Reed.Scripts.Nodes;

/// <summary>
/// 常驻「身上着火」特效。
/// 火焰不再整体等比缩放：按抗性条宽度铺开 1~5 个火焰簇，纵向高度按生物受击框封顶，
/// 再叠加地辉、余烬与火花。每个火焰簇的噪声相位、缩放、倾角都独立抖动。
/// </summary>
public partial class NReedGroundFireVfx : Node2D
{
    private const int MaxClusters = 5;
    private const float BaseWidth = 240f;
    private const float BaseFlameHeight = 300f;

    private static readonly StringName _innerColor = new("InnerColor");
    private static readonly StringName _outerColor = new("OuterColor");
    private static readonly StringName _distortion1Panning = new("Distortion1Panning");
    private static readonly StringName _distortion2Panning = new("Distortion2Panning");
    private static readonly StringName _distortion1Strength = new("Distortion1Strength");
    private static readonly StringName _distortion2Strength = new("Distortion2Strength");
    private static readonly StringName _groundOrigin = new("GroundOrigin");
    private static readonly StringName _flameHeight = new("FlameHeight");

    /// <summary>只有底层这一张；原来的两层加法 sprite 已停用（交叠处会叠加爆亮）。</summary>
    private static readonly string[] _flameSprites = ["SteppedFireMix"];

    /// <summary>单簇火焰的材质档位：扭曲强度决定轮廓抖动幅度，镜像让轮廓再分叉。</summary>
    private static readonly MaterialVariant[] _materialVariants =
    [
        new(1.0f, false),
        new(0.86f, true),
        new(1.14f, false),
        new(1.26f, true),
    ];

    private static readonly string _scenePath = "res://Reed/scenes/vfx/ReedGroundFire.tscn";
    private static readonly string _clusterPath = "res://Reed/scenes/vfx/ReedFireCluster.tscn";

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<Tween> _idleTweens = [];

    private Tween? _tween;
    private Node2D _flameRow = null!;
    private Node2D[] _clusters = [];
    private Sprite2D _groundGlow = null!;
    private GpuParticles2D _ember = null!;
    private GpuParticles2D _cinders = null!;
    private GpuParticles2D _sparks = null!;
    private NCreature? _creatureNode;
    private Creature _creatureBelonged = null!;
    private VfxColor _vfxColor;
    private Color _glowColor = new(1f, 0.35f, 0.06f);
    private Color _particleTint = Colors.White;
    private Color _baseModulate = Colors.White;
    private ClusterVariant[] _variants = [];
    private Node2D?[] _steps = [];
    private Vector2[] _basePositions = [];
    private float[] _activityPhase = [];
    private int _activeCount = 1;
    private float _footprint = BaseWidth;
    private float _swayTime;
    private bool _noTriggerTwice;

    public Creature CreatureBelonged => _creatureBelonged;

    public static IEnumerable<string> AssetPaths => [_scenePath, _clusterPath];

    private readonly record struct ClusterVariant(
        float ScaleX,
        float ScaleY,
        float OffsetX,
        float OffsetY,
        float Rotation,
        float Tint,
        float NoisePhase);

    private readonly record struct MaterialVariant(
        float Distortion,
        bool Flip);

    public static NReedGroundFireVfx? Create(Creature target, VfxColor color = VfxColor.Red)
    {
        if (TestMode.IsOn)
        {
            return null;
        }

        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(target);
        if (creatureNode == null)
        {
            return null;
        }

        NReedGroundFireVfx nGroundFireVfx = PreloadManager.Cache.GetScene(_scenePath).Instantiate<NReedGroundFireVfx>(PackedScene.GenEditState.Disabled);
        nGroundFireVfx._creatureBelonged = target;
        nGroundFireVfx._creatureNode = creatureNode;
        nGroundFireVfx._vfxColor = color;
        nGroundFireVfx.GlobalPosition = creatureNode.GetBottomOfHitbox();
        return nGroundFireVfx;
    }

    public override void _Ready()
    {
        _flameRow = GetNode<Node2D>("FlameRow");
        _clusters = [.. _flameRow.GetChildren().OfType<Node2D>()];
        _steps = new Node2D?[_clusters.Length];
        _basePositions = new Vector2[_clusters.Length];
        _activityPhase = new float[_clusters.Length];
        _groundGlow = GetNode<Sprite2D>("GroundGlow");
        _ember = GetNode<GpuParticles2D>("Ember");
        _cinders = GetNode<GpuParticles2D>("Cinders");
        _sparks = GetNode<GpuParticles2D>("Sparks");

        _baseModulate = Modulate;
        BuildVariants();
        ApplyColor();
        ApplyLayout();
        ApplyShaderVariation();
        TaskHelper.RunSafely(AnimateIn());
    }

    /// <summary>抗性条宽度变化时重新铺火焰；节点未就绪时先记账，_Ready 会统一应用。</summary>
    public void SetFootprintWidth(float width)
    {
        if (width <= 0f)
        {
            return;
        }
        if (Mathf.Abs(width - _footprint) < 0.5f)
        {
            return;
        }
        _footprint = width;
        if (_clusters.Length > 0)
        {
            ApplyLayout();
        }
    }

    public void FinishBurning()
    {
        if (_noTriggerTwice)
        {
            return;
        }
        _noTriggerTwice = true;
        TaskHelper.RunSafely(AnimateOut());
    }

    private void BuildVariants()
    {
        _rng.Randomize();
        _variants = new ClusterVariant[MaxClusters];
        for (int i = 0; i < MaxClusters; i++)
        {
            _variants[i] = new ClusterVariant(
                _rng.RandfRange(0.86f, 1.12f),
                _rng.RandfRange(0.85f, 1.08f),
                _rng.RandfRange(-0.1f, 0.1f),
                _rng.RandfRange(-10f, 12f),
                _rng.RandfRange(-0.05f, 0.05f),
                _rng.RandfRange(0.9f, 1.05f),
                _rng.RandfRange(0f, 10f));
            // 团数波动的相位与速度都随机，每簇每次生成都不一样，看不出循环。
            _activityPhase[i] = _rng.RandfRange(0f, Mathf.Tau);
        }
    }

    private void ApplyLayout()
    {
        if (_clusters.Length == 0)
        {
            return;
        }

        float width = _footprint > 0f ? _footprint : BaseWidth;
        float height = _creatureNode?.Hitbox?.Size.Y ?? 0f;
        if (height <= 0f)
        {
            height = width * 0.9f;
        }

        int count = Mathf.Clamp(1 + Mathf.FloorToInt(Mathf.Max(0f, width - 180f) / 150f), 1, MaxClusters);
        _activeCount = count;
        float spread = count <= 1 ? 0f : Mathf.Min(width * 0.7f, 640f);
        float spacing = count > 1 ? spread / (count - 1) : 0f;

        float widthScale = Mathf.Clamp(Mathf.Pow(width / BaseWidth, 0.25f), 0.8f, 1.2f);
        float heightScale = Mathf.Clamp(height / BaseFlameHeight * 0.72f, 0.55f, 1.2f);
        float baseScaleY = Mathf.Min(heightScale * Mathf.Clamp(widthScale, 0.95f, 1.08f), 1.15f);

        for (int i = 0; i < _clusters.Length; i++)
        {
            Node2D cluster = _clusters[i];
            bool active = i < count;
            SetClusterActive(cluster, active);
            if (!active)
            {
                continue;
            }

            ClusterVariant v = _variants[i];
            float t = count > 1 ? i / (float)(count - 1) * 2f - 1f : 0f;
            cluster.Position = new Vector2(t * spread * 0.5f + v.OffsetX * spacing, v.OffsetY);
            _basePositions[i] = cluster.Position;
            cluster.Rotation = v.Rotation;
            cluster.Scale = new Vector2(widthScale * v.ScaleX, baseScaleY * v.ScaleY);
            cluster.Modulate = new Color(v.Tint, v.Tint, v.Tint, 1f);
        }

        ApplyWorldAnchor(height * baseScaleY);

        float halfSpan = spread * 0.5f;
        float tipY = BaseFlameHeight * baseScaleY;

        _groundGlow.Scale = new Vector2((spread + 260f) / 128f, 0.26f + 0.1f * baseScaleY);
        _groundGlow.Modulate = WithAlpha(_glowColor, 0.28f + 0.08f * baseScaleY);

        _cinders.Position = new Vector2(0f, -55f * baseScaleY);
        _cinders.Amount = 5 + count * 2;
        SetBox(_cinders, halfSpan + 70f, 14f);

        _sparks.Position = new Vector2(0f, -tipY * 0.55f);
        _sparks.Amount = 3 + count * 2;
        SetBox(_sparks, halfSpan + 70f, 16f);

        _ember.Position = new Vector2(0f, -60f * baseScaleY);
        _ember.Amount = 12 + count * 4;
        SetBox(_ember, halfSpan + 80f, 10f);

        StartIdleAnimation();
    }

    /// <summary>
    /// 把「火焰根部」与「火焰总高度」按世界坐标告诉每一簇的底层材质。
    /// 共享 shader 用它们在所有簇之间算出统一的竖直渐变，并让噪声按世界坐标连续采样。
    /// </summary>
    private void ApplyWorldAnchor(float flameHeight)
    {
        Vector2 origin = GlobalPosition;
        float height = Mathf.Max(60f, flameHeight * 1.2f);
        foreach (Node2D cluster in _clusters)
        {
            if (!cluster.Visible)
            {
                continue;
            }
            if (cluster.GetNodeOrNull<Node2D>("Mask/StepFire")?.GetNodeOrNull<Node2D>("SteppedFireMix")?.Material
                is ShaderMaterial mix)
            {
                mix.SetShaderParameter(_groundOrigin, origin);
                mix.SetShaderParameter(_flameHeight, height);
            }
        }
    }

    /// <summary>每簇独立的低频呼吸与摇曳，周期错开，让火焰不会像一张静止的贴图。</summary>
    private void StartIdleAnimation()
    {
        foreach (Tween tween in _idleTweens)
        {
            tween.Kill();
        }
        _idleTweens.Clear();

        for (int i = 0; i < _clusters.Length; i++)
        {
            Node2D cluster = _clusters[i];
            if (!cluster.Visible)
            {
                continue;
            }

            Vector2 baseScale = cluster.Scale;
            float baseRotation = cluster.Rotation;
            float half = _rng.RandfRange(0.55f, 0.85f);
            float pulse = _rng.RandfRange(0.015f, 0.035f);
            float sway = _rng.RandfRange(0.006f, 0.018f) * (_rng.Randf() < 0.5f ? -1f : 1f);

            Tween tween = CreateTween().SetLoops();
            tween.TweenProperty(cluster, "scale", baseScale * (1f + pulse), half)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.Parallel().TweenProperty(cluster, "rotation", baseRotation + sway, half)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.TweenProperty(cluster, "scale", baseScale, half)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tween.Parallel().TweenProperty(cluster, "rotation", baseRotation, half)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _idleTweens.Add(tween);
        }
    }

    private void ApplyColor()
    {
        FirePalette palette = PaletteFor(_vfxColor);
        _glowColor = palette.Glow;
        _particleTint = palette.ParticleTint;

        _cinders.Modulate = _particleTint;
        _sparks.Modulate = _particleTint;
        _ember.Modulate = _particleTint;

        if (_vfxColor == VfxColor.Red)
        {
            return;
        }

        foreach (Node2D cluster in _clusters)
        {
            Node2D? step = cluster.GetNodeOrNull<Node2D>("Mask/StepFire");
            if (step == null)
            {
                continue;
            }
            if (step.GetNodeOrNull<Node2D>("SteppedFireMix")?.Material is ShaderMaterial mix)
            {
                mix.SetShaderParameter(_outerColor, palette.Outer);
                mix.SetShaderParameter(_innerColor, palette.Inner);
            }
        }
    }

    /// <summary>
    /// 每簇换一套轮廓档位（扭曲相位/强度、左右镜像），让并排的火焰不像是同一张图复制出来的。
    /// 噪声相位不能在这里改：共享 shader 的世界花纹要求所有簇取同一份，一旦按簇错开就会露出缝。
    /// 材质是 resource_local_to_scene，读到的是各自的原始值，可安全地按倍率改。
    /// </summary>
    private void ApplyShaderVariation()
    {
        for (int i = 0; i < _clusters.Length; i++)
        {
            _steps[i] = _clusters[i].GetNodeOrNull<Node2D>("Mask/StepFire");
            if (_steps[i] is not { } step)
            {
                continue;
            }

            MaterialVariant mv = _materialVariants[i % _materialVariants.Length];
            float p = _variants[i].NoisePhase;
            foreach (string spriteName in _flameSprites)
            {
                if (step.GetNodeOrNull<Sprite2D>(spriteName) is not { } sprite)
                {
                    continue;
                }
                sprite.FlipH = mv.Flip;
                if (sprite.Material is not ShaderMaterial material)
                {
                    continue;
                }

                material.SetShaderParameter(_distortion1Panning, new Vector2(p * 0.06f, 0.5f + p * 0.02f));
                material.SetShaderParameter(_distortion2Panning, new Vector2(0.04f + p * 0.005f, 0.7f + p * 0.008f));

                material.SetShaderParameter(_distortion1Strength, material.GetShaderParameter(_distortion1Strength).AsSingle() * mv.Distortion);
                material.SetShaderParameter(_distortion2Strength, material.GetShaderParameter(_distortion2Strength).AsSingle() * mv.Distortion);
            }

        }
    }

    /// <summary>
    /// 燃烧动效：
    /// 1) 每簇左右慢晃（整簇平移，脚下的地辉不跟着动）；
    /// 2) 每簇倾斜摇摆：skew 只影响 StepFire 自己的子节点，越靠上位移越大，读起来像火苗被风吹；
    /// 3) 团数波动：外侧簇只做很小幅度（≤10%）的透明度起伏，中间簇在簇数多时更轻微，
    ///    看起来是「火在忽明忽暗地烧」，而不是某一簇直接消失。
    /// </summary>
    public override void _Process(double delta)
    {
        _swayTime += (float)delta;
        for (int i = 0; i < _steps.Length; i++)
        {
            Node2D cluster = _clusters[i];
            if (!cluster.Visible)
            {
                continue;
            }

            float phase = i * 1.7f;

            // 左右晃动：两段不同频率的正弦叠加，避免看出周期。
            float driftAmp = 5f + 2.5f * (i % 3);
            float driftSpeed = 0.62f + 0.13f * (i % 4);
            float drift = driftAmp * (Mathf.Sin(_swayTime * driftSpeed + phase)
                + 0.35f * Mathf.Sin(_swayTime * driftSpeed * 1.9f + phase * 1.3f));
            cluster.Position = _basePositions[i] + new Vector2(drift, 0f);

            // 团数波动：同一套包络既驱动透明度，也在 0 时把整簇藏掉。
            float activity = ClusterActivity(i);
            float tint = _variants[i].Tint;
            cluster.Modulate = new Color(tint, tint, tint, activity);

            if (_steps[i] is not { } step)
            {
                continue;
            }
            float amp = 0.018f + 0.007f * (i % 3);
            float speed = 0.75f + 0.16f * (i % 4);
            step.Skew = amp * (Mathf.Sin(_swayTime * speed + phase) + 0.35f * Mathf.Sin(_swayTime * speed * 1.7f + phase * 1.3f));
        }
    }

    /// <summary>
    /// 单簇的「活跃度」：只在外侧簇上做很小幅度的透明度起伏（最多 10%），
    /// 读起来是火焰在忽明忽暗地烧，而不是某一簇消失。中间簇几乎不参与。
    /// 两个不可通约的频率叠加，周期不固定，看不到循环。
    /// </summary>
    private float ClusterActivity(int i)
    {
        int count = _activeCount;
        if (count < 2)
        {
            return 1f;
        }

        bool outer = i == 0 || i == count - 1;
        bool inner = count >= 4 && (i == 1 || i == count - 2);
        if (!outer && !inner)
        {
            return 1f;
        }

        float depth = outer ? 0.10f : 0.05f;
        float phase = _activityPhase[i];
        float a = 0.47f + 0.021f * i;
        float b = 1.13f + 0.047f * i;
        float wave = 0.5f * (Mathf.Sin(_swayTime * a + phase) + Mathf.Sin(_swayTime * b + phase * 1.7f));
        return 1f - depth * (0.5f + 0.5f * wave);
    }

    private static void SetClusterActive(Node2D cluster, bool active)
    {
        cluster.Visible = active;
        foreach (Node node in cluster.FindChildren("*", "GPUParticles2D", true, false))
        {
            if (node is GpuParticles2D particles)
            {
                particles.Emitting = active;
            }
        }
    }

    private static void SetBox(GpuParticles2D particles, float halfWidth, float halfHeight)
    {
        if (particles.ProcessMaterial is ParticleProcessMaterial material)
        {
            material.EmissionBoxExtents = new Vector3(Mathf.Max(halfWidth, 10f), halfHeight, 1f);
        }
    }

    private static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    private static FirePalette PaletteFor(VfxColor color) => color switch
    {
        VfxColor.Green => new FirePalette(new Color("2fa800"), new Color("06a000"), new Color("541b00"),
            new Color("2fa800"), new Color("c8ff8a")),
        VfxColor.Blue => new FirePalette(new Color("0099cd"), new Color("00a3bf"), Colors.Black,
            new Color("00b7e0"), new Color("a6f0ff")),
        VfxColor.Purple => new FirePalette(new Color("7821ff"), new Color("3f21ff"), new Color("541b00"),
            new Color("8b3cff"), new Color("e2b8ff")),
        VfxColor.Black or VfxColor.White => new FirePalette(Colors.White, Colors.White, new Color("541b00"),
            new Color("ffe6c4"), new Color("fff1d6")),
        _ => new FirePalette(Colors.White, Colors.White, new Color("541b00"),
            new Color(1f, 0.35f, 0.06f), Colors.White),
    };

    private readonly record struct FirePalette(Color Outer, Color Inner, Color Dark, Color Glow, Color ParticleTint);

    public override void _ExitTree()
    {
        _tween?.Kill();
        foreach (Tween tween in _idleTweens)
        {
            tween.Kill();
        }
        _idleTweens.Clear();
    }

    private async Task AnimateIn()
    {
        _tween?.Kill();
        float glowAlpha = _groundGlow.Modulate.A;
        Modulate = WithAlpha(_baseModulate, 0f);
        _flameRow.Scale = new Vector2(0.85f, 0.35f);
        _groundGlow.Modulate = WithAlpha(_groundGlow.Modulate, 0f);

        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(this, "modulate:a", _baseModulate.A, 0.4).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenProperty(_flameRow, "scale", Vector2.One, 0.65).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        _tween.TweenProperty(_groundGlow, "modulate:a", glowAlpha, 0.5).SetEase(Tween.EaseType.Out);
        await _tween.AwaitFinished(this);
    }

    private async Task AnimateOut()
    {
        _tween?.Kill();
        _ember.Restart();
        Task emberDone = _ember.AwaitSignal(GpuParticles2D.SignalName.Finished, this);
        _cinders.Emitting = false;
        _sparks.Emitting = false;

        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_flameRow, "modulate:a", 0f, 0.55);
        _tween.TweenProperty(_flameRow, "scale", new Vector2(0.9f, 0.55f), 0.6).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        _tween.TweenProperty(_groundGlow, "modulate:a", 0f, 0.5);
        _tween.TweenProperty(_cinders, "modulate:a", 0f, 0.4);
        _tween.TweenProperty(_sparks, "modulate:a", 0f, 0.4);
        if (await _tween.AwaitFinished(this))
        {
            foreach (Node2D cluster in _clusters)
            {
                SetClusterActive(cluster, false);
            }
            await emberDone;
            if (!IsInsideTree())
            {
                this.QueueFreeSafely();
                return;
            }
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            this.QueueFreeSafely();
        }
    }
}
