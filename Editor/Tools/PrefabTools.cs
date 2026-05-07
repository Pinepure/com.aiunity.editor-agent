#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AiUnity.EditorAgent
{
    internal static class PrefabTools
    {
        [Serializable]
        private sealed class PrefabCreateArgs
        {
            public string prefabPath;
            public bool overwrite;
            public PrefabNode root;
        }

        [Serializable]
        private sealed class PrefabNode
        {
            public string name;
            public string primitive;
            public bool inactive;
            public TransformSpec transform;
            public ComponentSpec[] components;
            public PrefabNode[] children;
        }

        [Serializable]
        private sealed class TransformSpec
        {
            public float[] position;
            public float[] rotationEuler;
            public float[] scale;
        }

        [Serializable]
        private sealed class ComponentSpec
        {
            public string type;
            public string json;
        }

        [AiTool(
            "prefab.create_from_json",
            "Creates a prefab from a JSON hierarchy manifest. Supports primitives, transforms, children, and component JSON patches.",
            @"{""type"":""object"",""properties"":{""prefabPath"":{""type"":""string""},""overwrite"":{""type"":""boolean""},""root"":{""type"":""object""}}}",
            @"{""type"":""object"",""properties"":{""prefabPath"":{""type"":""string""},""guid"":{""type"":""string""}}}",
            Danger = AiToolDanger.Medium
        )]
        public static string CreateFromJson(string argsJson)
        {
            PrefabCreateArgs args = AiJson.FromJsonOrThrow<PrefabCreateArgs>(argsJson);
            if (args == null) throw new Exception("Arguments are required.");
            if (string.IsNullOrEmpty(args.prefabPath)) throw new Exception("prefabPath is required.");
            if (args.root == null) throw new Exception("root is required.");

            string prefabPath = AiEditorAgentPaths.NormalizeAssetPath(args.prefabPath);
            if (!AiEditorAgentPaths.IsSafeAssetPath(prefabPath, true, ".prefab"))
            {
                throw new Exception("prefabPath must be a safe Assets/*.prefab path.");
            }

            string existingGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (!string.IsNullOrEmpty(existingGuid) && !args.overwrite)
            {
                throw new Exception("Prefab already exists. Set overwrite=true to replace it: " + prefabPath);
            }

            GameObject root = null;
            try
            {
                EnsureAssetFolder(prefabPath);
                root = CreateNode(args.root);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (saved == null)
                {
                    throw new Exception("PrefabUtility.SaveAsPrefabAsset returned null.");
                }

                AssetDatabase.ImportAsset(prefabPath);
                AssetDatabase.SaveAssets();
                string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                return "{\"prefabPath\":" + AiJson.Quote(prefabPath) + ",\"guid\":" + AiJson.Quote(guid) + ",\"rootName\":" + AiJson.Quote(root.name) + "}";
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static GameObject CreateNode(PrefabNode node)
        {
            if (node == null) node = new PrefabNode();
            GameObject go = CreatePrimitiveOrEmpty(node.primitive);
            go.name = string.IsNullOrEmpty(node.name) ? "AI_PrefabNode" : node.name;
            go.SetActive(!node.inactive);
            ApplyTransform(go.transform, node.transform);
            ApplyComponents(go, node.components);

            if (node.children != null)
            {
                for (int i = 0; i < node.children.Length; i++)
                {
                    GameObject child = CreateNode(node.children[i]);
                    child.transform.SetParent(go.transform, false);
                }
            }

            return go;
        }

        private static GameObject CreatePrimitiveOrEmpty(string primitive)
        {
            string p = string.IsNullOrEmpty(primitive) ? "empty" : primitive.Trim().ToLowerInvariant();
            if (p == "cube") return GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (p == "sphere") return GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (p == "capsule") return GameObject.CreatePrimitive(PrimitiveType.Capsule);
            if (p == "cylinder") return GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            if (p == "plane") return GameObject.CreatePrimitive(PrimitiveType.Plane);
            if (p == "quad") return GameObject.CreatePrimitive(PrimitiveType.Quad);
            return new GameObject("AI_PrefabNode");
        }

        private static void ApplyTransform(Transform transform, TransformSpec spec)
        {
            if (spec == null) return;
            if (spec.position != null && spec.position.Length >= 3)
            {
                transform.localPosition = new Vector3(spec.position[0], spec.position[1], spec.position[2]);
            }
            if (spec.rotationEuler != null && spec.rotationEuler.Length >= 3)
            {
                transform.localEulerAngles = new Vector3(spec.rotationEuler[0], spec.rotationEuler[1], spec.rotationEuler[2]);
            }
            if (spec.scale != null && spec.scale.Length >= 3)
            {
                transform.localScale = new Vector3(spec.scale[0], spec.scale[1], spec.scale[2]);
            }
        }

        private static void ApplyComponents(GameObject go, ComponentSpec[] components)
        {
            if (components == null) return;
            for (int i = 0; i < components.Length; i++)
            {
                ComponentSpec spec = components[i];
                if (spec == null || string.IsNullOrEmpty(spec.type)) continue;

                Type type = AiTypeUtility.FindType(spec.type);
                if (type == null)
                {
                    throw new Exception("Component type not found: " + spec.type);
                }

                if (!typeof(Component).IsAssignableFrom(type))
                {
                    throw new Exception("Type is not a Unity Component: " + spec.type);
                }

                Component component;
                if (typeof(Transform).IsAssignableFrom(type))
                {
                    component = go.transform;
                }
                else
                {
                    component = go.GetComponent(type);
                    if (component == null)
                    {
                        if (type.IsAbstract) throw new Exception("Cannot add abstract component: " + type.FullName);
                        component = go.AddComponent(type);
                    }
                }

                if (!string.IsNullOrEmpty(spec.json))
                {
                    EditorJsonUtility.FromJsonOverwrite(spec.json, component);
                }
            }
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string absolute = AiEditorAgentPaths.ToAbsolutePathFromAssetPath(assetPath);
            string dir = Path.GetDirectoryName(absolute);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }
    }
}
#endif
