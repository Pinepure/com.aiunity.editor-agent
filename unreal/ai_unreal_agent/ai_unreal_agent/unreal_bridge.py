import json
import os
import traceback

import unreal


def _asset_data_summary(asset_path, asset_data, include_tags=False):
    asset_class = ""
    if hasattr(asset_data, "asset_class_path"):
        class_path = asset_data.asset_class_path
        asset_class = str(class_path.asset_name) if hasattr(class_path, "asset_name") else str(class_path)
    elif hasattr(asset_data, "asset_class"):
        asset_class = str(asset_data.asset_class)
    payload = {
        "assetPath": asset_path,
        "assetName": str(getattr(asset_data, "asset_name", "")),
        "packageName": str(getattr(asset_data, "package_name", "")),
        "objectPath": str(getattr(asset_data, "object_path", asset_path)),
        "assetClass": asset_class,
    }
    if include_tags:
        tags = {}
        for key, value in getattr(asset_data, "tags_and_values", {}).items():
            tags[str(key)] = str(value)
        payload["tags"] = tags
    return payload


def _actor_summary(actor):
    location = actor.get_actor_location()
    rotation = actor.get_actor_rotation()
    scale = actor.get_actor_scale3d()
    return {
        "label": actor.get_actor_label(),
        "className": actor.get_class().get_name(),
        "pathName": actor.get_path_name(),
        "location": [location.x, location.y, location.z],
        "rotation": [rotation.roll, rotation.pitch, rotation.yaw],
        "scale": [scale.x, scale.y, scale.z],
    }


def _get_editor_world():
    if hasattr(unreal, "EditorLevelLibrary"):
        try:
            return unreal.EditorLevelLibrary.get_editor_world()
        except Exception:
            pass
    if hasattr(unreal, "UnrealEditorSubsystem"):
        subsystem = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
        if subsystem and hasattr(subsystem, "get_editor_world"):
            return subsystem.get_editor_world()
    return None


def _get_all_level_actors():
    if hasattr(unreal, "EditorActorSubsystem"):
        subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
        if subsystem and hasattr(subsystem, "get_all_level_actors"):
            return list(subsystem.get_all_level_actors())
    return list(unreal.EditorLevelLibrary.get_all_level_actors())


def _find_actor(args):
    actor_label = args.get("actorLabel", "")
    actor_path = args.get("actorPath", "")
    for actor in _get_all_level_actors():
        if actor_label and actor.get_actor_label() == actor_label:
            return actor
        if actor_path and actor.get_path_name() == actor_path:
            return actor
    raise RuntimeError(f"Actor not found. actorLabel={actor_label!r} actorPath={actor_path!r}")


def _save_current_level():
    try:
        return bool(unreal.EditorLevelLibrary.save_current_level())
    except Exception:
        if hasattr(unreal, "LevelEditorSubsystem"):
            subsystem = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
            if subsystem and hasattr(subsystem, "save_current_level"):
                return bool(subsystem.save_current_level())
        raise


def project_summary(args):
    world = _get_editor_world()
    return {
        "projectFile": unreal.Paths.get_project_file_path() if hasattr(unreal.Paths, "get_project_file_path") else "",
        "projectDir": unreal.Paths.project_dir(),
        "contentDir": unreal.Paths.project_content_dir(),
        "currentLevel": world.get_path_name() if world else None,
        "actorCount": len(_get_all_level_actors()),
    }


def assets_list(args):
    directory_path = args.get("directoryPath", "/Game")
    recursive = bool(args.get("recursive", True))
    include_tags = bool(args.get("includeTags", False))
    scan_limit = int(args.get("scanLimit") or 2000)
    paths = unreal.EditorAssetLibrary.list_assets(directory_path, recursive=recursive, include_folder=False)
    class_name = args.get("className", "")
    result = []
    for asset_path in paths:
        asset_data = unreal.EditorAssetLibrary.find_asset_data(asset_path)
        summary = _asset_data_summary(asset_path, asset_data, include_tags=include_tags)
        if class_name and summary["assetClass"] != class_name:
            continue
        result.append(summary)
        if len(result) >= scan_limit:
            break
    return result


def asset_get(args):
    asset_path = args["assetPath"]
    if not unreal.EditorAssetLibrary.does_asset_exist(asset_path):
        raise RuntimeError(f"Asset does not exist: {asset_path}")
    asset_data = unreal.EditorAssetLibrary.find_asset_data(asset_path)
    return _asset_data_summary(asset_path, asset_data, include_tags=True)


def level_actors_list(args):
    class_name = args.get("className", "")
    scan_limit = int(args.get("scanLimit") or 2000)
    result = []
    for actor in _get_all_level_actors():
        summary = _actor_summary(actor)
        if class_name and summary["className"] != class_name:
            continue
        result.append(summary)
        if len(result) >= scan_limit:
            break
    return result


def actor_get(args):
    actor = _find_actor(args)
    summary = _actor_summary(actor)
    summary["folderPath"] = actor.get_folder_path().to_string() if hasattr(actor.get_folder_path(), "to_string") else str(actor.get_folder_path())
    return summary


def actor_set_label(args):
    actor = _find_actor(args)
    actor.set_actor_label(args["newLabel"])
    saved = _save_current_level() if bool(args.get("saveCurrentLevel", True)) else False
    return {"actor": actor_get({"actorPath": actor.get_path_name()}), "savedCurrentLevel": saved}


def actor_set_transform(args):
    actor = _find_actor(args)
    if args.get("location") is not None:
        location = args["location"]
        actor.set_actor_location(unreal.Vector(float(location[0]), float(location[1]), float(location[2])), False, True)
    if args.get("rotation") is not None:
        rotation = args["rotation"]
        actor.set_actor_rotation(unreal.Rotator(float(rotation[0]), float(rotation[1]), float(rotation[2])), False)
    if args.get("scale") is not None:
        scale = args["scale"]
        actor.set_actor_scale3d(unreal.Vector(float(scale[0]), float(scale[1]), float(scale[2])))
    saved = _save_current_level() if bool(args.get("saveCurrentLevel", True)) else False
    return {"actor": actor_get({"actorPath": actor.get_path_name()}), "savedCurrentLevel": saved}


def asset_save(args):
    asset_path = args["assetPath"]
    saved = unreal.EditorAssetLibrary.save_asset(asset_path, only_if_is_dirty=bool(args.get("onlyIfDirty", False)))
    return {"assetPath": asset_path, "saved": bool(saved)}


OPERATIONS = {
    "project_summary": project_summary,
    "assets_list": assets_list,
    "asset_get": asset_get,
    "level_actors_list": level_actors_list,
    "actor_get": actor_get,
    "actor_set_label": actor_set_label,
    "actor_set_transform": actor_set_transform,
    "asset_save": asset_save,
}


def main():
    payload_path = os.environ["AI_UNREAL_AGENT_PAYLOAD"]
    output_path = os.environ["AI_UNREAL_AGENT_OUTPUT"]
    try:
        with open(payload_path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        operation = payload["operation"]
        args = payload.get("args", {})
        if operation not in OPERATIONS:
            raise RuntimeError(f"Unsupported operation: {operation}")
        result = OPERATIONS[operation](args)
        response = {"ok": True, "result": result}
    except Exception as error:  # pragma: no cover - executed inside Unreal
        response = {"ok": False, "error": str(error), "traceback": traceback.format_exc()}
    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(response, handle, indent=2, ensure_ascii=True)


if __name__ == "__main__":
    main()
