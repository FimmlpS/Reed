extends Node2D

## 临时预览脚本：复刻 NReedGroundFireVfx 的布局算法，方便在编辑器里试不同体型。
@export var footprint: float = 240.0
@export var creature_height: float = 300.0
## true = 只用一簇火焰横向拉宽盖住整个足迹（单一 alpha 场，簇与簇之间不会出现各自的边）。
@export var single_flame: bool = false
## 底层火焰轮廓的 alpha 过渡区间（越小越硬，越大越柔）；越大边缘越不像剪纸。
@export var mix_outer_step: Vector2 = Vector2(0.135, 0.175)
## 两个加法叠加层的整体透明度。
@export var add_self_alpha: float = 0.5
## true = 完全不用多簇，改由一张覆盖整个足迹的宽幅 sprite 生成单一 alpha 场。
@export var wide_flame: bool = false
## 宽幅模式：底部竖直偏置的强度 / 曲线指数（越大顶部越散）。
@export var wide_mask_strength: float = 0.55
@export var wide_mask_power: float = 1.5
## 宽幅模式：轮廓阈值区间（越窄越硬）。
@export var wide_outer_step: Vector2 = Vector2(0.135, 0.175)
## 宽幅模式：噪声增益（越高焰舌越碎、越多）。
@export var wide_noise_gain: float = 1.0
## 宽幅模式：内焰加亮强度。
@export var wide_core_strength: float = 0.5
## false = 去掉两层加法 sprite，内焰亮度改由 mix 层内部的 CoreStrength 提供。
@export var add_layers: bool = true
@export var core_strength: float = 0.0
## true = 底层改用世界坐标版的 shader：颜色按世界高度、噪声跨簇连续，交叠处只有 alpha 并集。
@export var shared_shader: bool = false
## true = 底层换回原版 VisualShader（改动前的参数），用来做同尺寸下的对照。
@export var legacy_mix: bool = false
## 每簇的横向缩放倍率（越小，各簇之间的谷越深、焰舌越分明）。
@export var cluster_width_scale: float = 1.0
## 簇的横向铺开比例（相对 footprint）。
@export var spread_ratio: float = 0.7
## 共享 shader 的颜色渐变区间（0=顶部, 1=根部）：低于 x 是 OuterColor。
@export var shared_inner_step: Vector2 = Vector2(0.26, 0.75)
## 共享 shader：底部填充权重 / 噪声对 alpha 的权重 / 颜色里噪声占比 / 整体不透明度。
@export var fill_weight: float = 0.85
@export var noise_weight: float = 1.0
@export var height_mix: float = 0.35
@export var alpha_scale: float = 0.85
## 外缘描边：alpha 低于 rim_softness 的部分压向 OuterColor*rim_darken（仅 color_from_v=false 时用）。
@export var rim_softness: float = 0.8
@export var rim_darken: float = 0.55
## true = 原版按 v 配色（每簇各自的红边会交叉）；false = 世界空间配色 + alpha 描边（交叠处无缝）。
@export var color_from_v: bool = true
## shader 调试视图：1=shape 2=noise 3=fill 4=v 5=hgt 7=body2。
@export var debug_mode: int = 0

const SHARED_SHADER := "res://Reed/images/vfx/fire/vfx_stepped_fire_shared.gdshader"
const LEGACY_SHADER := "res://Reed/images/vfx/fire/vfx_stepped_shader_fire_add.tres"
## 改动前 SteppedFireMix 的原始参数（取自 git HEAD 的 ReedStepFire.tscn）。
const LEGACY_PARAMS := {
	"OuterColor": Color(1, 0.166667, 0, 1),
	"InnerColor": Color(0.9, 0.40125, 0.045, 1),
	"InnerColorStep": Vector2(0.26, 0.75),
	"OuterStep": Vector2(0.135, 0.175),
	"Noise1Texture": preload("res://Reed/images/vfx/fire/triangle_noise_tile.png"),
	"Noise1Strength": 1.0,
	"Noise1Scaling": Vector2(2, 2),
	"Noise1Panning": Vector2(1, 0.7),
	"Noise2Texture": preload("res://Reed/images/vfx/fire/basic_fire_noise.png"),
	"Noise2Strength": 1.0,
	"Noise2Scaling": Vector2(1.4, 1.4),
	"Noise2Panning": Vector2(-0.2, 0.9),
	"NoiseMask": preload("res://Reed/images/vfx/fire/fire_bottom_mask.png"),
	"NoiseMaskScale": Vector2(1, 1.4),
	"NoiseMaskOffset": Vector2(0, 0.235),
	"InvertNoiseMask": true,
	"Distortion1Texture": preload("res://Reed/images/vfx/fire/triangle_noise_tile.png"),
	"Distortion1Strength": 0.095,
	"Distortion1Scale": Vector2(1, 1),
	"Distortion1Panning": Vector2(0.23, 0.8),
	"Distortion2Texture": preload("res://Reed/images/vfx/fire/zigzag_fire_distortion.png"),
	"Distortion2Strength": 0.1,
	"Distortion2Scale": Vector2(1, 1),
	"Distortion2Panning": Vector2(0, 0.76),
}
const _COPIED_PARAMS := [
	"OuterColor", "InnerColor", "InnerColorStep", "OuterStep",
	"Noise1Texture", "Noise2Texture", "Distortion1Texture", "Distortion2Texture", "BottomMask",
	"Noise1Strength", "Noise2Strength", "Distortion1Strength", "Distortion2Strength",
	"Distortion1Scale", "Distortion2Scale",
	"Distortion1Panning", "Distortion2Panning",
	"BottomMaskScale", "BottomMaskOffset",
]

var _ground_origin := Vector2.ZERO
var _flame_height := 300.0

## 火焰外观变体：噪声尺度（火焰舌头的粗细）、扭曲强度、遮罩高度、是否左右镜像。
## 同一套材质并排时形状会过于一致，靠这几档拉开差异。
const VARIANTS := [
	{"noise": 1.0, "dist": 1.0, "mask": 1.0, "flip": false},
	{"noise": 0.84, "dist": 0.86, "mask": 1.03, "flip": true},
	{"noise": 1.3, "dist": 1.14, "mask": 0.98, "flip": false},
	{"noise": 0.94, "dist": 1.26, "mask": 1.02, "flip": true},
]

var _idle_tweens: Array[Tween] = []
var _steps: Array[Node2D] = []
var _sway_time := 0.0


func _ready() -> void:
	_layout()


func _process(delta: float) -> void:
	_sway_time += delta
	for i in _steps.size():
		var step := _steps[i]
		if not is_instance_valid(step) or not step.is_visible_in_tree():
			continue
		var amp := 0.018 + 0.007 * float(i % 3)
		var speed := 0.75 + 0.16 * float(i % 4)
		var phase := float(i) * 1.7
		step.skew = amp * (sin(_sway_time * speed + phase) + 0.35 * sin(_sway_time * speed * 1.7 + phase * 1.3))


func _layout() -> void:
	for t in _idle_tweens:
		t.kill()
	_idle_tweens.clear()
	_steps.clear()

	var row: Node2D = $FlameRow
	var clusters := row.get_children()
	var width := footprint
	var height := creature_height

	var count := 1 if single_flame else clampi(1 + int(floor(maxf(0.0, width - 180.0) / 150.0)), 1, 5)
	var spread := 0.0 if count <= 1 else minf(width * spread_ratio, 640.0)
	var spacing := 0.0 if count <= 1 else spread / float(count - 1)
	var stretch := clampf(width / 240.0, 1.0, 4.0) if single_flame else 1.0
	var width_scale := clampf(pow(width / 240.0, 0.25), 0.8, 1.2)
	var height_scale := clampf(height / 300.0 * 0.72, 0.55, 1.2)
	var base_sy := minf(height_scale * clampf(width_scale, 0.95, 1.08), 1.15)

	var rng := RandomNumberGenerator.new()
	rng.seed = 20261025

	_ground_origin = global_position
	_flame_height = maxf(60.0, creature_height * base_sy * 1.2)

	if wide_flame:
		_apply_wide(width, height)
		for c in clusters:
			c.visible = false
			for p in c.find_children("*", "GPUParticles2D", true, false):
				p.emitting = false
		_layout_ambient(width, 0.0, clampf(height / 300.0 * 0.72, 0.55, 1.2))
		return

	var wide := get_node_or_null("WideFire") as Sprite2D
	if wide:
		wide.visible = false

	for i in clusters.size():
		var c: Node2D = clusters[i]
		var active := i < count
		c.visible = active
		for p in c.find_children("*", "GPUParticles2D", true, false):
			p.emitting = active
		if not active:
			continue

		var sx := rng.randf_range(0.86, 1.12)
		var sy := rng.randf_range(0.85, 1.08)
		var ox := rng.randf_range(-0.1, 0.1)
		var oy := rng.randf_range(-10.0, 12.0)
		var rot := rng.randf_range(-0.05, 0.05)
		var tint := rng.randf_range(0.9, 1.05)
		var t := 0.0 if count <= 1 else float(i) / float(count - 1) * 2.0 - 1.0

		c.position = Vector2(t * spread * 0.5 + ox * spacing, oy)
		c.rotation = rot
		c.scale = Vector2(width_scale * sx * stretch * cluster_width_scale, base_sy * sy)
		c.modulate = Color(tint, tint, tint, 1.0)
		_start_idle(c, rng)

		var step := c.get_node_or_null("Mask/StepFire") as Node2D
		if step:
			_steps.append(step)
			_apply_variant(step, i, stretch)

	_layout_ambient(width, spread, base_sy)


func _layout_ambient(width: float, spread: float, base_sy: float) -> void:
	var half := maxf(spread, width * 0.5) * 0.5
	var tip := 300.0 * base_sy
	var count := 1 if spread <= 0.0 else clampi(1 + int(floor(maxf(0.0, width - 180.0) / 150.0)), 1, 5)

	var glow_w := spread if spread > 0.0 else width
	$GroundGlow.scale = Vector2((glow_w + 260.0) / 128.0, 0.26 + 0.1 * base_sy)
	$GroundGlow.modulate = Color(1.0, 0.35, 0.06, 0.28 + 0.08 * base_sy)

	$Cinders.position = Vector2(0, -55.0 * base_sy)
	$Cinders.amount = 5 + count * 2
	_set_box($Cinders, half + 70.0, 14.0)

	$Sparks.position = Vector2(0, -tip * 0.55)
	$Sparks.amount = 3 + count * 2
	_set_box($Sparks, half + 70.0, 16.0)

	$Ember.position = Vector2(0, -60.0 * base_sy)
	$Ember.amount = 12 + count * 4
	_set_box($Ember, half + 80.0, 10.0)


func _apply_variant(step: Node2D, i: int, stretch: float = 1.0) -> void:
	var v: Dictionary = VARIANTS[i % VARIANTS.size()]
	var p := float(i)
	if legacy_mix:
		var legacy_sprite := step.get_node_or_null("SteppedFireMix") as Sprite2D
		if legacy_sprite:
			legacy_sprite.material = _legacy_material(step)
	elif shared_shader:
		var mix_sprite := step.get_node_or_null("SteppedFireMix") as Sprite2D
		if mix_sprite:
			mix_sprite.material = _shared_material(step, mix_sprite)
	for sprite_name in ["SteppedFireMix", "SteppedFireAdd", "SteppedFireAdd1"]:
		var sprite := step.get_node_or_null(sprite_name) as Sprite2D
		if sprite == null:
			continue
		sprite.flip_h = bool(v["flip"])
		var m := sprite.material as ShaderMaterial
		if m == null:
			continue
		if not shared_shader:
			# 共享 shader 下噪声必须是全簇同一份，否则图案在世界空间里又断开了
			m.set_shader_parameter("Noise1Panning", Vector2(0.3 + p * 0.17, 0.5 + p * 0.06))
			m.set_shader_parameter("Noise2Panning", Vector2(-0.3 + p * 0.15, 0.4 + p * 0.09))
		m.set_shader_parameter("Distortion1Panning", Vector2(p * 0.11, 0.5 + p * 0.03))
		m.set_shader_parameter("Distortion2Panning", Vector2(0.04 + p * 0.02, 0.7 + p * 0.03))

		if not shared_shader:
			var noise_mult := float(v["noise"])
			var n1 = m.get_shader_parameter("Noise1Scaling")
			var n2 = m.get_shader_parameter("Noise2Scaling")
			# 横向拉宽时补偿噪声频率，否则焰舌会跟着被拉长
			if n1 is Vector2:
				m.set_shader_parameter("Noise1Scaling", Vector2(n1.x * noise_mult * stretch, n1.y * noise_mult))
			if n2 is Vector2:
				m.set_shader_parameter("Noise2Scaling", Vector2(n2.x * noise_mult * stretch, n2.y * noise_mult))
			for dname in ["Distortion1Scale", "Distortion2Scale"]:
				var dv: Vector2 = m.get_shader_parameter(dname)
				m.set_shader_parameter(dname, Vector2(dv.x * stretch, dv.y))

		var dist_mult := float(v["dist"])
		var d1: float = m.get_shader_parameter("Distortion1Strength")
		var d2: float = m.get_shader_parameter("Distortion2Strength")
		m.set_shader_parameter("Distortion1Strength", d1 * dist_mult)
		m.set_shader_parameter("Distortion2Strength", d2 * dist_mult)

		if not shared_shader:
			var mask_mult := float(v["mask"])
			var ms_val = m.get_shader_parameter("NoiseMaskScale")
			if ms_val is Vector2:
				m.set_shader_parameter("NoiseMaskScale", Vector2(ms_val.x, ms_val.y * mask_mult))

	var mix := step.get_node_or_null("SteppedFireMix") as Sprite2D
	if mix and mix.material is ShaderMaterial:
		var mm := mix.material as ShaderMaterial
		mm.set_shader_parameter("OuterStep", LEGACY_PARAMS["OuterStep"] if legacy_mix else mix_outer_step)
		mm.set_shader_parameter("CoreStrength", core_strength)

	for sprite_name in ["SteppedFireAdd", "SteppedFireAdd1"]:
		var add := step.get_node_or_null(sprite_name) as Sprite2D
		if add:
			add.visible = add_layers
			var sc := add.self_modulate
			sc.a = add_self_alpha
			add.self_modulate = sc


func _start_idle(c: Node2D, rng: RandomNumberGenerator) -> void:
	var base_scale := c.scale
	var base_rot := c.rotation
	var half := rng.randf_range(0.55, 0.85)
	var pulse := rng.randf_range(0.015, 0.035)
	var sway := rng.randf_range(0.006, 0.018) * (1.0 if rng.randf() < 0.5 else -1.0)
	var tw := create_tween().set_loops()
	tw.tween_property(c, "scale", base_scale * (1.0 + pulse), half).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	tw.parallel().tween_property(c, "rotation", base_rot + sway, half).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	tw.tween_property(c, "scale", base_scale, half).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	tw.parallel().tween_property(c, "rotation", base_rot, half).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	_idle_tweens.append(tw)


func _legacy_material(step: Node2D) -> ShaderMaterial:
	var cached: ShaderMaterial = step.get_meta("legacy_mat") if step.has_meta("legacy_mat") else null
	if cached == null:
		cached = ShaderMaterial.new()
		cached.shader = load(LEGACY_SHADER)
		for k in LEGACY_PARAMS:
			cached.set_shader_parameter(k, LEGACY_PARAMS[k])
		step.set_meta("legacy_mat", cached)
	return cached


func _shared_material(step: Node2D, sprite: Sprite2D) -> ShaderMaterial:
	var cached: ShaderMaterial = step.get_meta("shared_mat") if step.has_meta("shared_mat") else null
	if cached == null:
		var src := sprite.material as ShaderMaterial
		cached = ShaderMaterial.new()
		cached.shader = load(SHARED_SHADER)
		for k in _COPIED_PARAMS:
			var val = src.get_shader_parameter(k)
			if val != null:
				cached.set_shader_parameter(k, val)
		# 旧材质里这张图叫 NoiseMask；新材质已经有 BottomMask，不能再拿 NoiseMask 覆盖成 null
		if cached.get_shader_parameter("BottomMask") == null:
			cached.set_shader_parameter("BottomMask", src.get_shader_parameter("NoiseMask"))
		step.set_meta("shared_mat", cached)
	cached.set_shader_parameter("GroundOrigin", _ground_origin)
	cached.set_shader_parameter("FlameHeight", _flame_height)
	cached.set_shader_parameter("InnerColorStep", shared_inner_step)
	cached.set_shader_parameter("Noise1Tile", Vector2(150.0, 150.0))
	cached.set_shader_parameter("Noise2Tile", Vector2(200.0, 200.0))
	cached.set_shader_parameter("FillWeight", fill_weight)
	cached.set_shader_parameter("NoiseWeight", noise_weight)
	cached.set_shader_parameter("HeightMix", height_mix)
	cached.set_shader_parameter("AlphaScale", alpha_scale)
	cached.set_shader_parameter("RimSoftness", rim_softness)
	cached.set_shader_parameter("RimDarken", rim_darken)
	cached.set_shader_parameter("ColorFromV", color_from_v)
	cached.set_shader_parameter("DebugMode", debug_mode)
	return cached


func _apply_wide(width: float, height: float) -> void:
	var wide := get_node_or_null("WideFire") as Sprite2D
	if wide == null:
		wide = Sprite2D.new()
		wide.name = "WideFire"
		var mat := ShaderMaterial.new()
		mat.shader = load("res://Reed/images/vfx/fire/vfx_stepped_fire_wide.gdshader")
		mat.set_shader_parameter("Noise1Texture", load("res://Reed/images/vfx/fire/triangle_noise_tile.png"))
		mat.set_shader_parameter("Noise2Texture", load("res://Reed/images/vfx/fire/basic_fire_noise.png"))
		mat.set_shader_parameter("Distortion1Texture", load("res://Reed/images/vfx/fire/triangle_noise_tile.png"))
		mat.set_shader_parameter("Distortion2Texture", load("res://Reed/images/vfx/fire/zigzag_fire_distortion.png"))
		wide.material = mat
		# 贴图本身不参与形状（shader 不看 TEXTURE），只用来撑出 sprite 尺寸
		wide.texture = load("res://Reed/images/vfx/fire/fire_base_pointy.png")
		add_child(wide)
		move_child(wide, $GroundGlow.get_index() + 1)

	wide.visible = true
	wide.position = Vector2(0, -height * 0.5)
	wide.scale = Vector2(width / 500.0, height / 500.0)

	var m := wide.material as ShaderMaterial
	# 横向频率按世界宽度补偿：每约 250px 一个噪声块，拉宽后焰舌不会跟着变胖
	var aspect := width / 500.0
	m.set_shader_parameter("Noise1Scaling", Vector2(2.0 * aspect, 2.0))
	m.set_shader_parameter("Noise2Scaling", Vector2(1.4 * aspect, 1.4))
	m.set_shader_parameter("Distortion1Scale", Vector2(1.0 * aspect, 1.0))
	m.set_shader_parameter("Distortion2Scale", Vector2(1.0 * aspect, 1.0))
	m.set_shader_parameter("NoiseGain", wide_noise_gain)
	m.set_shader_parameter("MaskStrength", wide_mask_strength)
	m.set_shader_parameter("MaskPower", wide_mask_power)
	m.set_shader_parameter("OuterStep", wide_outer_step)
	m.set_shader_parameter("CoreStrength", wide_core_strength)


func _set_box(p: GPUParticles2D, half_width: float, half_height: float) -> void:
	var m := p.process_material as ParticleProcessMaterial
	if m:
		m.emission_box_extents = Vector3(maxf(half_width, 10.0), half_height, 1.0)
