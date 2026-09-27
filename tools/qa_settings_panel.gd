extends SceneTree

func _initialize() -> void:
	call_deferred("run_check")

func run_check() -> void:
	var main_scene = load("res://Main.tscn")
	if main_scene == null:
		push_error("Main scene failed to load")
		quit(1)
		return
	var main_instance = main_scene.instantiate()
	if main_instance == null or main_instance.get_node_or_null("SettingsPanel") == null:
		push_error("Settings panel is missing from Main")
		quit(1)
		return
	var preview = main_instance.get_node_or_null("Hud/FacePanel/FacePreview")
	if preview == null or preview.get_node_or_null("CameraImage") == null or preview.get_node_or_null("FaceOverlay") == null:
		push_error("Camera image and landmark overlay are not independent")
		quit(1)
		return
	main_instance.free()
	var panel = load("res://SettingsPanel.tscn").instantiate()
	if panel.scale.x >= 1.0 or panel.get_node("PreviewToggle/Glyph").text != "TOGGLE" or panel.get_node("ScreenModeToggle/Glyph").text != "TOGGLE":
		push_error("The compact toggle panel is misconfigured")
		quit(1)
		return
	if panel.get_node("ScreenModeValue").text != "DUAL":
		push_error("Screen mode value is missing")
		quit(1)
		return
	root.add_child(panel)
	await physics_frame
	var names = ["SensitivityMinus", "SensitivityPlus", "MouseMinus", "MousePlus", "RadiusMinus", "RadiusPlus", "PreviewToggle", "ScreenModeToggle"]
	var origin = Vector3(0, 1.65, 5)
	for name in names:
		var button = panel.get_node(name)
		var direction = (button.global_position - origin).normalized()
		var query = PhysicsRayQueryParameters3D.create(origin, button.global_position + direction)
		var hit = panel.get_world_3d().direct_space_state.intersect_ray(query)
		if hit.get("collider") != button:
			push_error("Ray did not hit " + name)
			quit(1)
			return
	print("Settings panel loaded; all eight buttons have hittable collision")
	quit()
