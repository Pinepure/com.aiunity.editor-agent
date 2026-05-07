#if UNITY_EDITOR
using System;
using System.Reflection;

namespace AiUnity.EditorAgent
{
    internal static class AiTypeUtility
    {
        public static Type FindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            Type direct = Type.GetType(typeName, false);
            if (direct != null) return direct;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t = null;
                try
                {
                    t = assemblies[i].GetType(typeName, false);
                }
                catch
                {
                }
                if (t != null) return t;
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch
                {
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    Type t = types[j];
                    if (t == null) continue;
                    if (t.Name == typeName || t.FullName == typeName) return t;
                }
            }

            return null;
        }
    }
}
#endif
