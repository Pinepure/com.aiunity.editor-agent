import json
import math
import sys
import traceback

import bpy


def _vector(values):
    return [round(float(value), 6) for value in values]


def _object_summary(obj):
    return {
        "name": obj.name,
        "type": obj.type,
        "location": _vector(obj.location),
        "rotationEulerDegrees": [round(math.degrees(value), 6) for value in obj.rotation_euler],
        "scale": _vector(obj.scale),
        "hideViewport": bool(obj.hide_viewport),
        "hideRender": bool(obj.hide_render),
    }


def _resolve_scene(scene_name):
    if scene_name:
        if scene_name not in bpy.data.scenes:
            raise RuntimeError(f"Unknown scene: {scene_name}")
        return bpy.data.scenes[scene_name]
    return bpy.context.scene


def _resolve_object(object_name):
    if object_name not in bpy.data.objects:
        raise RuntimeError(f"Unknown object: {object_name}")
    return bpy.data.objects[object_name]


def version_get(args):
    return {
        "versionString": bpy.app.version_string,
        "version": list(bpy.app.version),
        "background": bool(bpy.app.background),
        "blendFile": bpy.data.filepath,
    }


def file_summary(args):
    current_path = bpy.data.filepath
    scenes = list(bpy.data.scenes)
    objects = list(bpy.data.objects)
    collections = list(bpy.data.collections)
    return {
        "blendFile": current_path,
        "sceneCount": len(scenes),
        "objectCount": len(objects),
        "collectionCount": len(collections),
        "sceneNames": [scene.name for scene in scenes],
    }


def scene_summary_get(args):
    scene = _resolve_scene(args.get("scene", ""))
    return {
        "name": scene.name,
        "renderEngine": scene.render.engine,
        "frameCurrent": scene.frame_current,
        "frameStart": scene.frame_start,
        "frameEnd": scene.frame_end,
        "objectCount": len(scene.objects),
        "collectionName": scene.collection.name if scene.collection else "",
    }


def scenes_list(args):
    return [
        {
            "name": scene.name,
            "renderEngine": scene.render.engine,
            "frameCurrent": scene.frame_current,
            "frameStart": scene.frame_start,
            "frameEnd": scene.frame_end,
            "objectCount": len(scene.objects),
        }
        for scene in bpy.data.scenes
    ]


def collections_list(args):
    return [
        {
            "name": collection.name,
            "objectCount": len(collection.objects),
            "allObjectCount": len(collection.all_objects),
            "childCount": len(collection.children),
        }
        for collection in bpy.data.collections
    ]


def objects_list(args):
    scene_name = args.get("scene", "")
    collection_name = args.get("collection", "")
    object_type = args.get("type", "")
    include_hidden = bool(args.get("includeHidden", False))
    scan_limit = int(args.get("scanLimit") or 2000)

    if collection_name:
        if collection_name not in bpy.data.collections:
            raise RuntimeError(f"Unknown collection: {collection_name}")
        candidates = list(bpy.data.collections[collection_name].all_objects)
    elif scene_name:
        candidates = list(_resolve_scene(scene_name).objects)
    else:
        candidates = list(bpy.data.objects)

    result = []
    for obj in candidates:
        if object_type and obj.type != object_type:
            continue
        if not include_hidden and (obj.hide_viewport or obj.hide_render):
            continue
        result.append(_object_summary(obj))
        if len(result) >= scan_limit:
            break
    return result


def object_get(args):
    obj = _resolve_object(args["objectName"])
    return {
        "name": obj.name,
        "type": obj.type,
        "location": _vector(obj.location),
        "rotationEulerDegrees": [round(math.degrees(value), 6) for value in obj.rotation_euler],
        "scale": _vector(obj.scale),
        "dimensions": _vector(obj.dimensions),
        "hideViewport": bool(obj.hide_viewport),
        "hideRender": bool(obj.hide_render),
        "parent": obj.parent.name if obj.parent else None,
        "dataName": obj.data.name if obj.data else None,
        "materialSlots": [slot.material.name if slot.material else None for slot in obj.material_slots],
        "modifiers": [modifier.name for modifier in obj.modifiers],
        "collections": [collection.name for collection in obj.users_collection],
    }


def _save_mutation(args):
    output_blend = args.get("outputBlendFile", "")
    in_place = bool(args.get("inPlace", False))
    if output_blend:
        bpy.ops.wm.save_as_mainfile(filepath=output_blend)
        return {
            "savedTo": output_blend,
            "inPlace": False,
        }
    if in_place:
        if not bpy.data.filepath:
            raise RuntimeError("Cannot save in place because the current file has no filepath.")
        bpy.ops.wm.save_mainfile()
        return {
            "savedTo": bpy.data.filepath,
            "inPlace": True,
        }
    raise RuntimeError("Mutation requires outputBlendFile or inPlace=true.")


def object_transform_set(args):
    obj = _resolve_object(args["objectName"])
    if "location" in args and args["location"] is not None:
        values = args["location"]
        if len(values) != 3:
            raise RuntimeError("location must contain exactly 3 numbers.")
        obj.location = values
    if "rotationEulerDegrees" in args and args["rotationEulerDegrees"] is not None:
        values = args["rotationEulerDegrees"]
        if len(values) != 3:
            raise RuntimeError("rotationEulerDegrees must contain exactly 3 numbers.")
        obj.rotation_mode = "XYZ"
        obj.rotation_euler = [math.radians(float(value)) for value in values]
    if "scale" in args and args["scale"] is not None:
        values = args["scale"]
        if len(values) != 3:
            raise RuntimeError("scale must contain exactly 3 numbers.")
        obj.scale = values
    save_result = _save_mutation(args)
    return {
        "object": object_get({"objectName": obj.name}),
        "save": save_result,
    }


def render_still(args):
    output_path = args["outputPath"]
    scene = _resolve_scene(args.get("scene", ""))
    frame = args.get("frame")
    if frame is not None:
        scene.frame_set(int(frame))
    if args.get("resolutionX") is not None:
        scene.render.resolution_x = int(args["resolutionX"])
    if args.get("resolutionY") is not None:
        scene.render.resolution_y = int(args["resolutionY"])
    scene.render.filepath = output_path
    bpy.ops.render.render(write_still=True, scene=scene.name)
    return {
        "scene": scene.name,
        "outputPath": output_path,
        "frame": scene.frame_current,
        "renderEngine": scene.render.engine,
        "resolution": [scene.render.resolution_x, scene.render.resolution_y],
    }


OPERATIONS = {
    "version_get": version_get,
    "file_summary": file_summary,
    "scene_summary_get": scene_summary_get,
    "scenes_list": scenes_list,
    "collections_list": collections_list,
    "objects_list": objects_list,
    "object_get": object_get,
    "object_transform_set": object_transform_set,
    "render_still": render_still,
}


def main():
    payload_path = sys.argv[-2]
    output_path = sys.argv[-1]
    try:
        with open(payload_path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        operation = payload["operation"]
        args = payload.get("args", {})
        if operation not in OPERATIONS:
            raise RuntimeError(f"Unsupported operation: {operation}")
        result = OPERATIONS[operation](args)
        response = {
            "ok": True,
            "result": result,
        }
    except Exception as error:  # pragma: no cover - executed inside Blender
        response = {
            "ok": False,
            "error": str(error),
            "traceback": traceback.format_exc(),
        }
    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(response, handle, indent=2, ensure_ascii=True)


if __name__ == "__main__":
    main()
