extends Node

## 临时：把共享 shader 的中间量分张渲染出来，定位哪里不对。
@export var lab_path: NodePath = ^"../Lab"
@export var delay: float = 0.8


func _ready() -> void:
	var lab := get_node_or_null(lab_path)
	lab.shared_shader = true
	lab.add_layers = false
	lab.core_strength = 0.35
	lab.mix_outer_step = Vector2(0.11, 0.22)
	lab._layout()
	for mode in [4, 5, 6]:
		for c in lab.get_node("FlameRow").get_children():
			var step := c.get_node_or_null("Mask/StepFire")
			if step == null:
				continue
			var mix := step.get_node_or_null("SteppedFireMix")
			if mix and mix.material is ShaderMaterial:
				(mix.material as ShaderMaterial).set_shader_parameter("DebugMode", mode)
		await get_tree().create_timer(delay).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("res://.tmp_dbg_%d.png" % mode)
	get_tree().quit()
