import json
import traceback

import maya.standalone

maya.standalone.initialize(name="python")

import maya.cmds as cmds  # noqa: E402


def _load_scene(payload):
    scene_file = payload.get("sceneFile", "")
    if scene_file:
        cmds.file(scene_file, open=True, force=True)
    else:
        cmds.file(new=True, force=True)
    return scene_file


def _node_summary(node):
    return {
        "name": node,
        "type": cmds.nodeType(node),
        "longName": cmds.ls(node, long=True)[0],
        "parent": (cmds.listRelatives(node, parent=True, fullPath=True) or [None])[0],
        "childCount": len(cmds.listRelatives(node, children=True, fullPath=True) or []),
    }


def scene_summary(args):
    return {
        "sceneFile": cmds.file(query=True, sceneName=True),
        "isModified": bool(cmds.file(query=True, modified=True)),
        "dagNodeCount": len(cmds.ls(dag=True, long=True) or []),
        "dependencyNodeCount": len(cmds.ls(dependencyNodes=True, long=True) or []),
        "topLevelTransforms": cmds.ls(assemblies=True, long=True) or [],
    }


def nodes_list(args):
    node_type = args.get("nodeType", "")
    dag_only = bool(args.get("dagOnly", False))
    long_names = bool(args.get("longNames", True))
    scan_limit = int(args.get("scanLimit") or 2000)
    if dag_only:
        nodes = cmds.ls(dag=True, long=long_names) or []
    else:
        nodes = cmds.ls(long=long_names) or []
    result = []
    for node in nodes:
        if node_type and cmds.nodeType(node) != node_type:
            continue
        result.append(_node_summary(node))
        if len(result) >= scan_limit:
            break
    return result


def node_get(args):
    node = args["nodeName"]
    if not cmds.objExists(node):
        raise RuntimeError(f"Unknown node: {node}")
    summary = _node_summary(node)
    summary["attributes"] = cmds.listAttr(node) or []
    return summary


def attr_get(args):
    node = args["nodeName"]
    attr_name = args["attrName"]
    plug = f"{node}.{attr_name}"
    if not cmds.objExists(plug):
        raise RuntimeError(f"Unknown attribute: {plug}")
    value = cmds.getAttr(plug)
    return {"nodeName": node, "attrName": attr_name, "value": value}


def _save_mutation(args):
    output_scene = args.get("outputSceneFile", "")
    in_place = bool(args.get("inPlace", False))
    if output_scene:
        cmds.file(rename=output_scene)
        _save_current_scene(output_scene)
        return {"savedTo": output_scene, "inPlace": False}
    if in_place:
        scene_path = cmds.file(query=True, sceneName=True)
        if not scene_path:
            raise RuntimeError("Cannot save in place because the current scene has no filepath.")
        _save_current_scene(scene_path)
        return {"savedTo": scene_path, "inPlace": True}
    raise RuntimeError("Mutation requires outputSceneFile or inPlace=true.")


def attr_set(args):
    node = args["nodeName"]
    attr_name = args["attrName"]
    plug = f"{node}.{attr_name}"
    if not cmds.objExists(plug):
        raise RuntimeError(f"Unknown attribute: {plug}")
    if "values" in args and args["values"] is not None:
        cmds.setAttr(plug, *args["values"])
    else:
        cmds.setAttr(plug, args["value"])
    return {"attribute": attr_get({"nodeName": node, "attrName": attr_name}), "save": _save_mutation(args)}


def node_create(args):
    node_type = args["nodeType"]
    node_name = args.get("nodeName", "")
    parent = args.get("parentNode", "")
    created = cmds.createNode(node_type, name=node_name if node_name else None, parent=parent if parent else None)
    return {"node": node_get({"nodeName": created}), "save": _save_mutation(args)}


def node_delete(args):
    node = args["nodeName"]
    if not cmds.objExists(node):
        raise RuntimeError(f"Unknown node: {node}")
    cmds.delete(node)
    return {"deletedNode": node, "save": _save_mutation(args)}


def _save_current_scene(scene_path):
    lower = scene_path.lower()
    file_type = "mayaBinary" if lower.endswith(".mb") else "mayaAscii"
    cmds.file(save=True, type=file_type, force=True)


OPERATIONS = {
    "scene_summary": scene_summary,
    "nodes_list": nodes_list,
    "node_get": node_get,
    "attr_get": attr_get,
    "attr_set": attr_set,
    "node_create": node_create,
    "node_delete": node_delete,
}


def main():
    import sys

    payload_path = sys.argv[-2]
    output_path = sys.argv[-1]
    try:
        with open(payload_path, "r", encoding="utf-8") as handle:
            payload = json.load(handle)
        _load_scene(payload)
        operation = payload["operation"]
        args = payload.get("args", {})
        if operation not in OPERATIONS:
            raise RuntimeError(f"Unsupported operation: {operation}")
        result = OPERATIONS[operation](args)
        response = {"ok": True, "result": result}
    except Exception as error:  # pragma: no cover - executed inside Maya
        response = {"ok": False, "error": str(error), "traceback": traceback.format_exc()}
    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(response, handle, indent=2, ensure_ascii=True)


if __name__ == "__main__":
    main()
