extends Node

## 临时：把 viewport 按原生分辨率截一张图再退出，避免窗口被 DPI 缩放后看不清。
@export var out_path: String = "res://.tmp_shot.png"
@export var delay: float = 1.2


func _ready() -> void:
	await get_tree().create_timer(delay).timeout
	await RenderingServer.frame_post_draw
	var img := get_viewport().get_texture().get_image()
	img.save_png(out_path)
	get_tree().quit()
