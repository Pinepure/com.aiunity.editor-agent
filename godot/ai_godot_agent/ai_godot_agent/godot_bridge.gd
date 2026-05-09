extends SceneTree

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var payload_path := args[0]
	var output_path := args[1]
	var response := {}
	var file := FileAccess.open(payload_path, FileAccess.READ)
	if file == null:
		response = {"ok": false, "error": "Cannot open payload file: %s" % payload_path}
		_write_response(output_path, response)
		quit()
		return

	var payload = JSON.parse_string(file.get_as_text())
	if typeof(payload) != TYPE_DICTIONARY:
		response = {"ok": false, "error": "Payload JSON must be an object."}
		_write_response(output_path, response)
		quit()
		return

	var operation: String = payload.get("operation", "")
	var op_args: Dictionary = payload.get("args", {})
	var result = null
	if not _operations().has(operation):
		response = {"ok": false, "error": "Unsupported operation: %s" % operation}
	else:
		var callable: Callable = _operations()[operation]
		result = callable.call(op_args)
		response = {"ok": true, "result": result}
	_write_response(output_path, response)
	quit()


func _write_response(output_path: String, response: Dictionary) -> void:
	var output := FileAccess.open(output_path, FileAccess.WRITE)
	output.store_string(JSON.stringify(response, "\t"))


func _operations() -> Dictionary:
	return {
		"project_summary": Callable(self, "_project_summary"),
		"scenes_list": Callable(self, "_scenes_list"),
		"scene_nodes_list": Callable(self, "_scene_nodes_list"),
		"node_get": Callable(self, "_node_get"),
		"node_property_get": Callable(self, "_node_property_get"),
		"node_property_set": Callable(self, "_node_property_set"),
		"scene_add_node": Callable(self, "_scene_add_node"),
		"scene_delete_node": Callable(self, "_scene_delete_node"),
	}


func _project_summary(args: Dictionary) -> Dictionary:
	var scenes = _collect_files("res://", [".tscn", ".scn"])
	var scripts = _collect_files("res://", [".gd", ".cs"])
	return {
		"projectName": str(ProjectSettings.get_setting("application/config/name", "")),
		"mainScene": str(ProjectSettings.get_setting("application/run/main_scene", "")),
		"sceneCount": scenes.size(),
		"scriptCount": scripts.size(),
		"projectPath": ProjectSettings.globalize_path("res://"),
	}


func _scenes_list(args: Dictionary) -> Array:
	var scenes = _collect_files("res://", [".tscn", ".scn"])
	var result: Array = []
	for scene_path in scenes:
		result.append({"scenePath": scene_path})
	return result


func _scene_nodes_list(args: Dictionary) -> Array:
	var root = _instantiate_scene(args.get("scenePath", ""))
	var result: Array = []
	_collect_nodes(root, result)
	return result


func _node_get(args: Dictionary) -> Dictionary:
	var root = _instantiate_scene(args.get("scenePath", ""))
	var node_path: String = args.get("nodePath", ".")
	var node: Node = root if node_path == "." else root.get_node_or_null(NodePath(node_path))
	if node == null:
		push_error("Unknown node path: %s" % node_path)
		return {}
	return _node_summary(node)


func _node_property_get(args: Dictionary) -> Dictionary:
	var root = _instantiate_scene(args.get("scenePath", ""))
	var node: Node = root if args.get("nodePath", ".") == "." else root.get_node_or_null(NodePath(args.get("nodePath", ".")))
	if node == null:
		push_error("Unknown node path: %s" % str(args.get("nodePath", ".")))
		return {}
	var property_name: String = args.get("propertyName", "")
	return {
		"scenePath": args.get("scenePath", ""),
		"nodePath": args.get("nodePath", "."),
		"propertyName": property_name,
		"value": node.get(property_name),
	}


func _node_property_set(args: Dictionary) -> Dictionary:
	var root = _instantiate_scene(args.get("scenePath", ""))
	var node: Node = root if args.get("nodePath", ".") == "." else root.get_node_or_null(NodePath(args.get("nodePath", ".")))
	if node == null:
		push_error("Unknown node path: %s" % str(args.get("nodePath", ".")))
		return {}
	var property_name: String = args.get("propertyName", "")
	node.set(property_name, args.get("value"))
	var save = _save_scene(root, str(args.get("scenePath", "")), str(args.get("outputScenePath", "")), bool(args.get("inPlace", false)))
	return {"node": _node_summary(node), "save": save}


func _scene_add_node(args: Dictionary) -> Dictionary:
	var root = _instantiate_scene(args.get("scenePath", ""))
	var parent_path: String = args.get("parentPath", ".")
	var parent: Node = root if parent_path == "." else root.get_node_or_null(NodePath(parent_path))
	if parent == null:
		push_error("Unknown parent path: %s" % parent_path)
		return {}
	var class_name: String = args.get("nodeClass", "Node")
	var created = ClassDB.instantiate(class_name)
	if created == null or not (created is Node):
		push_error("Cannot instantiate node class: %s" % class_name)
		return {}
	var node: Node = created
	node.name = str(args.get("nodeName", class_name))
	parent.add_child(node)
	node.owner = root
	var save = _save_scene(root, str(args.get("scenePath", "")), str(args.get("outputScenePath", "")), bool(args.get("inPlace", false)))
	return {"node": _node_summary(node), "save": save}


func _scene_delete_node(args: Dictionary) -> Dictionary:
	var root = _instantiate_scene(args.get("scenePath", ""))
	var node_path: String = args.get("nodePath", "")
	var node: Node = root if node_path == "." else root.get_node_or_null(NodePath(node_path))
	if node == null:
		push_error("Unknown node path: %s" % node_path)
		return {}
	if node == root:
		push_error("Cannot delete the scene root.")
		return {}
	var deleted_name := node.name
	node.get_parent().remove_child(node)
	node.free()
	var save = _save_scene(root, str(args.get("scenePath", "")), str(args.get("outputScenePath", "")), bool(args.get("inPlace", false)))
	return {"deletedNodePath": node_path, "deletedNodeName": deleted_name, "save": save}


func _collect_files(root_path: String, extensions: Array) -> Array:
	var result: Array = []
	_walk_dir(root_path, result, extensions)
	return result


func _walk_dir(path: String, result: Array, extensions: Array) -> void:
	var dir = DirAccess.open(path)
	if dir == null:
		return
	dir.list_dir_begin()
	while true:
		var name = dir.get_next()
		if name == "":
			break
		if name.begins_with("."):
			continue
		var child = path.path_join(name)
		if dir.current_is_dir():
			_walk_dir(child, result, extensions)
		else:
			for ext in extensions:
				if child.ends_with(ext):
					result.append(child)
					break
	dir.list_dir_end()


func _instantiate_scene(scene_path: String) -> Node:
	var packed: PackedScene = load(scene_path)
	if packed == null:
		push_error("Cannot load scene: %s" % scene_path)
		return Node.new()
	var instance = packed.instantiate()
	instance.owner = instance
	return instance


func _collect_nodes(node: Node, result: Array) -> void:
	result.append(_node_summary(node))
	for child in node.get_children():
		if child is Node:
			_collect_nodes(child, result)


func _node_summary(node: Node) -> Dictionary:
	return {
		"name": node.name,
		"nodePath": str(node.get_path()),
		"class": node.get_class(),
		"childCount": node.get_child_count(),
		"ownerPath": str(node.owner.get_path()) if node.owner else null,
	}


func _save_scene(root: Node, source_scene_path: String, output_scene_path: String, in_place: bool) -> Dictionary:
	var target_path := output_scene_path
	if target_path == "":
		if not in_place:
			push_error("Mutation requires outputScenePath or inPlace=true.")
			return {}
		target_path = source_scene_path
	var packed := PackedScene.new()
	var err := packed.pack(root)
	if err != OK:
		push_error("Failed to pack scene. Error code: %s" % err)
		return {}
	err = ResourceSaver.save(packed, target_path)
	if err != OK:
		push_error("Failed to save scene. Error code: %s" % err)
		return {}
	return {"savedTo": target_path, "inPlace": target_path == source_scene_path}
