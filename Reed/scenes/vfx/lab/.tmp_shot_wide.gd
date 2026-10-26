extends Node

## 临时：依次套几档宽幅火焰参数，各截一张图后退出。
const PRESETS := [
	{"tag": "a", "mask_strength": 0.55, "mask_power": 1.5, "noise_gain": 1.0, "outer_step": Vector2(0.135, 0.175), "core": 0.5},
	{"tag": "b", "mask_strength": 0.45, "mask_power": 1.2, "noise_gain": 1.35, "outer_step": Vector2(0.100, 0.160), "core": 0.55},
	{"tag": "c", "mask_strength": 0.40, "mask_power": 1.0, "noise_gain": 1.6, "outer_step": Vector2(0.090, 0.140), "core": 0.6},
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
		lab.wide_mask_strength = p["mask_strength"]
		lab.wide_mask_power = p["mask_power"]
		lab.wide_noise_gain = p["noise_gain"]
		lab.wide_outer_step = p["outer_step"]
		lab.wide_core_strength = p["core"]
		lab._layout()
		await get_tree().create_timer(delay).timeout
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		img.save_png("res://.tmp_shot_wide_%s.png" % p["tag"])
	get_tree().quit()
