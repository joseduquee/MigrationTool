using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine.Catalogs
{
    public sealed class CatalogStore
    {
        private readonly Dictionary<int, JObject> _attributesById = new();
        private readonly Dictionary<string, JArray> _referenceById = new();
        private readonly List<string> _roles = new();

        public CatalogStore(string attributesCatalogPath, string referenceDataCatalogPath, string configurationPath)
        {
            // Attributes
            var attrJson = JToken.Parse(File.ReadAllText(attributesCatalogPath));
            if (attrJson is JArray attrs)
            {
                foreach (var a in attrs)
                {
                    var id = a.Value<int?>("id");
                    if (id is int i) _attributesById[i] = (JObject)a;
                }
            }

            // Reference data
            var refJson = JToken.Parse(File.ReadAllText(referenceDataCatalogPath));
            if (refJson is JArray refs)
            {
                foreach (var r in refs)
                {
                    var id = r.Value<string>("id");
                    var data = r["data"] as JArray ?? new JArray();
                    if (!string.IsNullOrEmpty(id)) _referenceById[id!] = data;
                }
            }

            // Configuration (roles)
            var confJson = JToken.Parse(File.ReadAllText(configurationPath));
            // Busca un array "roles" en cualquier parte razonable (config.roles o roles)
            var rolesToken = confJson.SelectToken("$.config.roles") ?? confJson.SelectToken("$.roles");
            if (rolesToken is JArray rolesArr)
            {
                foreach (var r in rolesArr)
                {
                    var label = r.ToString();
                    if (!string.IsNullOrWhiteSpace(label)) _roles.Add(label);
                }
            }
        }

        public JObject? GetAttributeById(int id) => _attributesById.TryGetValue(id, out var x) ? x : null;

        public IEnumerable<(string label, JToken value)> GetReferenceValues(string referenceId)
        {
            if (_referenceById.TryGetValue(referenceId, out var arr))
            {
                foreach (var it in arr)
                {
                    var label = it.Value<string>("value") ?? "";
                    var key = it["key"] ?? JValue.CreateNull();
                    yield return (label, key);
                }
            }
        }

        public bool TryGetReferenceValues(string referenceId, out List<(string, JToken)> values)
        {
            values = new List<(string, JToken)>();
            foreach (var item in GetReferenceValues(referenceId))
                values.Add(item);
            return values.Count > 0;
        }

        public List<string> GetRoles() => _roles;
    }
}
