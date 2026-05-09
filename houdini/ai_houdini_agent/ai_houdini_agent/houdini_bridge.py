import json
import traceback

import hou


def _node_summary(node):
    return {
        "path": node.path(),
        "name": node.name(),
        "typeName": node.type().name(),
        "typeNameWithCategory": node.type().nameWithCategory(),
        "description": node.type().description(),
        "isNetwork": bool(node.isNetwork()),
        "childCount": len(node.children()),
        "parmCount": len(node.parms()),
        "bypassed": bool(node.isBypassed()) if hasattr(node, "isBypassed") else False,
    }


def _resolve_node(node_path):
    node = hou.node(node_path)
    if node is None:
        raise RuntimeError(f"Unknown node path: {node_path}")
    return node


def _load_hip_file(payload):
    hip_file = payload.get("hipFile", "")
    if hip_file:
        hou.hipFile.load(hip_file, suppress_save_prompt=True, ignore_load_warnings=True)
    return hip_file


def version_get(args):
    return {
        "versionString": hou.applicationVersionString(),
        "version": list(hou.applicationVersion()),
        "licenseCategory": str(hou.licenseCategory()),
        "hipFile": hou.hipFile.path(),
    }


def file_summary(args):
    root = hou.node("/")
    descendants = list(root.allSubChildren())
    top_children = list(root.children())
    return {
        "hipFile": hou.hipFile.path(),
        "nodeCount": len(descendants),
        "topLevelNetworkCount": len(top_children),
        "topLevelNetworks": [child.path() for child in top_children],
        "isNewFile": bool(hou.hipFile.isNewFile()),
        "hasUnsavedChanges": bool(hou.hipFile.hasUnsavedChanges()),
    }


def root_networks_list(args):
    return [_node_summary(node) for node in hou.node("/").children()]


def node_children_list(args):
    parent = _resolve_node(args.get("nodePath", "/"))
    return [_node_summary(child) for child in parent.children()]


def nodes_list(args):
    parent = _resolve_node(args.get("rootPath", "/"))
    recursive = bool(args.get("recursive", False))
    scan_limit = int(args.get("scanLimit") or 2000)
    node_type_name = args.get("nodeTypeName", "")
    candidates = list(parent.allSubChildren()) if recursive else list(parent.children())
    result = []
    for node in candidates:
        if node_type_name and node.type().name() != node_type_name:
            continue
        result.append(_node_summary(node))
        if len(result) >= scan_limit:
            break
    return result


def node_get(args):
    node = _resolve_node(args["nodePath"])
    return {
        **_node_summary(node),
        "comment": node.comment(),
        "inputs": [input_node.path() if input_node else None for input_node in node.inputs()],
        "outputs": [output_node.path() for output_node in node.outputs()],
        "parmNames": [parm.name() for parm in node.parms()],
    }


def parm_get(args):
    node = _resolve_node(args["nodePath"])
    parm_name = args["parmName"]
    parm_tuple = node.parmTuple(parm_name)
    parm = node.parm(parm_name)
    if parm_tuple is None and parm is None:
        raise RuntimeError(f"Unknown parameter on {node.path()}: {parm_name}")
    if parm_tuple is not None and len(parm_tuple) > 1:
        values = [_safe_parm_value(item) for item in parm_tuple]
        expressions = [_safe_parm_expression(item) for item in parm_tuple]
        return {
            "nodePath": node.path(),
            "parmName": parm_name,
            "tupleSize": len(parm_tuple),
            "values": values,
            "expressions": expressions,
        }
    target = parm if parm is not None else parm_tuple[0]
    return {
        "nodePath": node.path(),
        "parmName": parm_name,
        "tupleSize": 1,
        "value": _safe_parm_value(target),
        "expression": _safe_parm_expression(target),
    }


def _save_mutation(args):
    output_hip = args.get("outputHipFile", "")
    in_place = bool(args.get("inPlace", False))
    if output_hip:
        hou.hipFile.save(file_name=output_hip)
        return {"savedTo": output_hip, "inPlace": False}
    if in_place:
        current_path = hou.hipFile.path()
        if not current_path:
            raise RuntimeError("Cannot save in place because the current file has no filepath.")
        hou.hipFile.save()
        return {"savedTo": current_path, "inPlace": True}
    raise RuntimeError("Mutation requires outputHipFile or inPlace=true.")


def parm_set(args):
    node = _resolve_node(args["nodePath"])
    parm_name = args["parmName"]
    parm_tuple = node.parmTuple(parm_name)
    parm = node.parm(parm_name)
    if parm_tuple is None and parm is None:
        raise RuntimeError(f"Unknown parameter on {node.path()}: {parm_name}")
    if "values" in args and args["values"] is not None:
        values = args["values"]
        if parm_tuple is None:
            raise RuntimeError(f"Parameter is not a tuple: {parm_name}")
        if len(values) != len(parm_tuple):
            raise RuntimeError(f"values length must match tuple size {len(parm_tuple)}")
        parm_tuple.set(values)
    elif "value" in args:
        target = parm if parm is not None else parm_tuple[0]
        target.set(args["value"])
    else:
        raise RuntimeError("Provide value or values.")
    return {"parm": parm_get({"nodePath": node.path(), "parmName": parm_name}), "save": _save_mutation(args)}


def node_create(args):
    parent = _resolve_node(args.get("parentPath", "/obj"))
    node_type_name = args["nodeTypeName"]
    node_name = args.get("nodeName", "")
    created = parent.createNode(node_type_name, node_name or None)
    if args.get("autoPosition", True):
        try:
            created.moveToGoodPosition()
        except Exception:
            pass
    return {"node": node_get({"nodePath": created.path()}), "save": _save_mutation(args)}


def node_delete(args):
    node = _resolve_node(args["nodePath"])
    parent_path = node.parent().path() if node.parent() else None
    node.destroy()
    return {"deletedNodePath": args["nodePath"], "parentPath": parent_path, "save": _save_mutation(args)}


def _safe_parm_value(parm):
    try:
        return parm.eval()
    except Exception:
        try:
            return parm.rawValue()
        except Exception:
            return None


def _safe_parm_expression(parm):
    try:
        return parm.expression()
    except Exception:
        return None


OPERATIONS = {
    "version_get": version_get,
    "file_summary": file_summary,
    "root_networks_list": root_networks_list,
    "node_children_list": node_children_list,
    "nodes_list": nodes_list,
    "node_get": node_get,
    "parm_get": parm_get,
    "parm_set": parm_set,
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
        _load_hip_file(payload)
        operation = payload["operation"]
        args = payload.get("args", {})
        if operation not in OPERATIONS:
            raise RuntimeError(f"Unsupported operation: {operation}")
        result = OPERATIONS[operation](args)
        response = {"ok": True, "result": result}
    except Exception as error:  # pragma: no cover - executed inside Houdini
        response = {"ok": False, "error": str(error), "traceback": traceback.format_exc()}
    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(response, handle, indent=2, ensure_ascii=True)


if __name__ == "__main__":
    main()
