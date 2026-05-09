#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiUnity.EditorAgent
{
    internal static class SceneTools
    {
        [Serializable]
        private sealed class CreateObjectArgs
        {
            public string name;
            public string primitive;
            public float[] position;
            public float[] rotationEuler;
            public float[] scale;
            public string parentPath;
        }

        [Serializable]
        private sealed class FindObjectsArgs
        {
            public string nameContains;
            public int maxResults;
        }

        [Serializable]
        private sealed class SelectAssetArgs
        {
            public string path;
        }

        [AiTool(
            "selection.get",
            "Returns the current Unity Editor selection, including asset paths and scene hierarchy paths when available.",
            "{}",
            @"{""type"":""object"",""properties"":{""objects"":{""type"":""array""}}}"
        )]
        public static string GetSelection(string argsJson)
        {
            UnityEngine.Object[] objects = Selection.objects;
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"count\":").Append(AiJson.Number(objects == null ? 0 : objects.Length)).Append(",\"objects\":[");
            if (objects != null)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    UnityEngine.Object obj = objects[i];
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    GameObject go = obj as GameObject;
                    if (go == null && obj is Component) go = ((Component)obj).gameObject;
                    sb.Append('{');
                    sb.Append("\"name\":").Append(AiJson.Quote(obj == null ? string.Empty : obj.name)).Append(',');
                    sb.Append("\"type\":").Append(AiJson.Quote(obj == null ? string.Empty : obj.GetType().FullName)).Append(',');
                    sb.Append("\"instanceId\":").Append(AiJson.Number(obj == null ? 0 : obj.GetInstanceID())).Append(',');
                    sb.Append("\"assetPath\":").Append(AiJson.Quote(assetPath)).Append(',');
                    sb.Append("\"scenePath\":").Append(AiJson.Quote(go == null ? string.Empty : GetHierarchyPath(go)));
                    sb.Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        [AiTool(
            "selection.set_asset",
            "Selects an asset by path in the Unity Editor.",
            @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}}}",
            @"{""type"":""object"",""properties"":{""selected"":{""type"":""boolean""}}}"
        )]
        public static string SetAssetSelection(string argsJson)
        {
            SelectAssetArgs args = AiJson.FromJsonOrThrow<SelectAssetArgs>(argsJson);
            if (args == null || string.IsNullOrEmpty(args.path)) throw new Exception("path is required.");
            string path = AiEditorAgentPaths.NormalizeAssetPath(args.path);
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj == null) throw new Exception("Asset not found: " + path);
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            return "{\"selected\":true,\"path\":" + AiJson.Quote(path) + "}";
        }

        [AiTool(
            "scene.create_empty",
            "Creates an empty GameObject in the active scene. Args: name, position, rotationEuler, scale, parentPath.",
            @"{""type"":""object"",""properties"":{""name"":{""type"":""string""},""position"":{""type"":""array""},""rotationEuler"":{""type"":""array""},""scale"":{""type"":""array""},""parentPath"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string CreateEmpty(string argsJson)
        {
            CreateObjectArgs args = AiJson.FromJsonOrThrow<CreateObjectArgs>(argsJson);
            if (args == null) args = new CreateObjectArgs();
            GameObject go = new GameObject(string.IsNullOrEmpty(args.name) ? "AI_GameObject" : args.name);
            ApplyTransformAndParent(go, args);
            Undo.RegisterCreatedObjectUndo(go, "AI Create Empty");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return SceneObjectJson(go);
        }

        [AiTool(
            "scene.create_primitive",
            "Creates a primitive GameObject in the active scene. Args: primitive, name, position, rotationEuler, scale, parentPath.",
            @"{""type"":""object"",""properties"":{""primitive"":{""type"":""string""},""name"":{""type"":""string""},""position"":{""type"":""array""},""rotationEuler"":{""type"":""array""},""scale"":{""type"":""array""},""parentPath"":{""type"":""string""}}}",
            @"{""type"":""object""}"
        )]
        public static string CreatePrimitive(string argsJson)
        {
            CreateObjectArgs args = AiJson.FromJsonOrThrow<CreateObjectArgs>(argsJson);
            if (args == null) args = new CreateObjectArgs();
            GameObject go = CreatePrimitiveOrDefault(args.primitive);
            go.name = string.IsNullOrEmpty(args.name) ? "AI_" + (string.IsNullOrEmpty(args.primitive) ? "Cube" : args.primitive) : args.name;
            ApplyTransformAndParent(go, args);
            Undo.RegisterCreatedObjectUndo(go, "AI Create Primitive");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            return SceneObjectJson(go);
        }

        [AiTool(
            "scene.find_objects",
            "Finds GameObjects in the active scene by name substring. Args: nameContains, maxResults.",
            @"{""type"":""object"",""properties"":{""nameContains"":{""type"":""string""},""maxResults"":{""type"":""integer""}}}",
            @"{""type"":""object"",""properties"":{""objects"":{""type"":""array""}}}"
        )]
        public static string FindObjects(string argsJson)
        {
            FindObjectsArgs args = AiJson.FromJsonOrThrow<FindObjectsArgs>(argsJson);
            if (args == null) args = new FindObjectsArgs();
            string needle = args.nameContains ?? string.Empty;
            int max = args.maxResults <= 0 ? 200 : Math.Min(args.maxResults, 5000);

            List<GameObject> all = GetActiveSceneObjects();
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"scene\":").Append(AiJson.Quote(SceneManager.GetActiveScene().name)).Append(",\"objects\":[");
            int written = 0;
            for (int i = 0; i < all.Count && written < max; i++)
            {
                GameObject go = all[i];
                if (!string.IsNullOrEmpty(needle) && go.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (written > 0) sb.Append(',');
                sb.Append(SceneObjectJson(go));
                written++;
            }
            sb.Append("],\"truncated\":").Append(AiJson.Bool(written >= max && all.Count > written)).Append('}');
            return sb.ToString();
        }

        [AiTool(
            "scene.save_open_scenes",
            "Saves currently open scenes.",
            "{}",
            @"{""type"":""object"",""properties"":{""saved"":{""type"":""boolean""}}}",
            Danger = AiToolDanger.Medium,
            RequiresConfirmation = true
        )]
        public static string SaveOpenScenes(string argsJson)
        {
            bool saved = EditorSceneManager.SaveOpenScenes();
            return "{\"saved\":" + AiJson.Bool(saved) + "}";
        }

        private static GameObject CreatePrimitiveOrDefault(string primitive)
        {
            string p = string.IsNullOrEmpty(primitive) ? "cube" : primitive.Trim().ToLowerInvariant();
            if (p == "sphere") return GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (p == "capsule") return GameObject.CreatePrimitive(PrimitiveType.Capsule);
            if (p == "cylinder") return GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            if (p == "plane") return GameObject.CreatePrimitive(PrimitiveType.Plane);
            if (p == "quad") return GameObject.CreatePrimitive(PrimitiveType.Quad);
            return GameObject.CreatePrimitive(PrimitiveType.Cube);
        }

        private static void ApplyTransformAndParent(GameObject go, CreateObjectArgs args)
        {
            if (args.position != null && args.position.Length >= 3) go.transform.position = new Vector3(args.position[0], args.position[1], args.position[2]);
            if (args.rotationEuler != null && args.rotationEuler.Length >= 3) go.transform.eulerAngles = new Vector3(args.rotationEuler[0], args.rotationEuler[1], args.rotationEuler[2]);
            if (args.scale != null && args.scale.Length >= 3) go.transform.localScale = new Vector3(args.scale[0], args.scale[1], args.scale[2]);
            if (!string.IsNullOrEmpty(args.parentPath))
            {
                GameObject parent = FindByHierarchyPath(args.parentPath);
                if (parent == null) throw new Exception("Parent not found: " + args.parentPath);
                go.transform.SetParent(parent.transform, true);
            }
        }

        private static string SceneObjectJson(GameObject go)
        {
            return "{\"name\":" + AiJson.Quote(go.name)
                + ",\"instanceId\":" + AiJson.Number(go.GetInstanceID())
                + ",\"scene\":" + AiJson.Quote(go.scene.name)
                + ",\"scenePath\":" + AiJson.Quote(GetHierarchyPath(go))
                + ",\"position\":" + AiJson.Vector3(go.transform.position)
                + ",\"rotationEuler\":" + AiJson.Vector3(go.transform.eulerAngles)
                + ",\"scale\":" + AiJson.Vector3(go.transform.localScale)
                + "}";
        }

        private static List<GameObject> GetActiveSceneObjects()
        {
            List<GameObject> result = new List<GameObject>();
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return result;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) AddRecursive(result, roots[i]);
            return result;
        }

        private static void AddRecursive(List<GameObject> result, GameObject go)
        {
            result.Add(go);
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++) AddRecursive(result, t.GetChild(i).gameObject);
        }

        private static GameObject FindByHierarchyPath(string path)
        {
            string normalized = (path ?? string.Empty).Trim('/');
            List<GameObject> all = GetActiveSceneObjects();
            for (int i = 0; i < all.Count; i++)
            {
                if (GetHierarchyPath(all[i]).Trim('/') == normalized) return all[i];
            }
            return null;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;
            List<string> names = new List<string>();
            Transform t = go.transform;
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names.ToArray());
        }
    }
}
#endif
