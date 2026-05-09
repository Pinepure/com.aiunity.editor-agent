#if UNITY_EDITOR
using System;

namespace AiUnity.EditorAgent
{
    public enum AiToolDanger
    {
        Low,
        Medium,
        High
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AiToolAttribute : Attribute
    {
        public string Id { get; private set; }
        public string Description { get; private set; }
        public string ArgsSchemaJson { get; private set; }
        public string ReturnSchemaJson { get; private set; }
        public AiToolDanger Danger { get; set; }
        public bool RequiresConfirmation { get; set; }

        public AiToolAttribute(string id, string description)
            : this(id, description, "{}", "{\"type\":\"object\"}")
        {
        }

        public AiToolAttribute(string id, string description, string argsSchemaJson)
            : this(id, description, argsSchemaJson, "{\"type\":\"object\"}")
        {
        }

        public AiToolAttribute(string id, string description, string argsSchemaJson, string returnSchemaJson)
        {
            Id = id;
            Description = description;
            ArgsSchemaJson = string.IsNullOrEmpty(argsSchemaJson) ? "{}" : argsSchemaJson;
            ReturnSchemaJson = string.IsNullOrEmpty(returnSchemaJson) ? "{}" : returnSchemaJson;
            Danger = AiToolDanger.Low;
            RequiresConfirmation = false;
        }
    }
}
#endif
