extends Node

## 临时：多簇模式下对比「世界坐标共享 shader」与原始加法版本。
const PRESETS := [
	{"tag": "b0", "add_layers": true, "shared": false, "core": 0.0, "outer": Vector2(0.135, 0.175), "ws": 1.0, "spread": 0.70},
	{"tag": "t1", "add_layers": false, "shared": true, "core": 0.30, "outer": Vector2(0.110, 0.220), "ws": 0.80, "spread": 0.85},
	{"tag": "t2", "add_layers": false, "shared": true, "core": 0.35, "outer": Vector2(0.110, 0.220), "ws": 0.90, "spread": 0.78},
	{"tag": "t3", "add_layers": false, "shared": true, "core": 0.25, "outer": Vector2(0.110, 0.220), "ws": 0.75, "spread": 0.90},
]

@export var lab_path: NodePath = ^"../Lab"
@export var delay: float = 1.0


func _ready() -> void:
	var lab := get_node_or_null(lab_path)
	if lab == null:
		push_error("lab not found")
		get_tree().quit(1)
		return
	for p in PRESETS:
		lab.add_layers = p["add_layers"]
		lab.shared_shader = p["shared"]
		lab.core_strength = p["core"]
		lab.mix_outer_step = p["outer"]
		lab.cluster_width_scale = p["ws"]
		lab.spread_ratio = p["spread"]
		lab._layout()
		await get_tree().create_timer(delay).timeout
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		img.save_png("res://.tmp_shot_multi_%s.png" % p["tag"])
	get_tree().quit()
