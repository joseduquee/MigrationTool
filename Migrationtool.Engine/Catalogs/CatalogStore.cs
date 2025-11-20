using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine.Catalogs
{
    public sealed class CatalogStore
    {
        private readonly Dictionary<int, JObject> _attributesById = new();
        private readonly Dictionary<string, JArray> _referenceById = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _roles = new();
        private readonly JToken _configuration;

        public CatalogStore(string attributesCatalogPath, string referenceDataCatalogPath, string configurationPath)
        {
            // ===== 1) ATTRIBUTES =====
            var attrRoot = JToken.Parse(File.ReadAllText(attributesCatalogPath));
            if (attrRoot is JArray attrs)
            {
                foreach (var a in attrs.OfType<JObject>())
                {
                    var id = a.Value<int?>("id");
                    if (id is int i) _attributesById[i] = a;
                }
            }

            // ===== 2) REFERENCE DATA =====
            var refText = File.ReadAllText(referenceDataCatalogPath);
            var refRoot = JToken.Parse(refText);

            void AddRefEntry(JToken r)
            {
                if (r is not JObject o) return;
                var id = o.Value<string>("id");
                var name = o.Value<string>("name");
                var data = o["data"] as JArray ?? new JArray();

                if (!string.IsNullOrWhiteSpace(id))
                    _referenceById[id!] = data;

                if (!string.IsNullOrWhiteSpace(name) && !_referenceById.ContainsKey(name!))
                    _referenceById[name!] = data;

                // ---- ALIAS conocidos (ajusta/añade los que necesites) ----
                // Si el catálogo trae "paisos" pero el atributo apunta a "paisosIso"
                if (string.Equals(id, "paisos", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "paisos", StringComparison.OrdinalIgnoreCase))
                {
                    _referenceById["paisosIso"] = data;
                }
            }

            // Formato A) raíz es array de entradas o nodos
            // Formato B) raíz es array de nodos que contienen "referenceDataCatalog": [ entradas ]
            if (refRoot is JArray rootArr)
            {
                foreach (var node in rootArr)
                {
                    var inner = node["referenceDataCatalog"] as JArray;
                    if (inner is not null)
                    {
                        foreach (var r in inner) AddRefEntry(r);
                    }
                    else
                    {
                        // Si ya viene plano como { id, name, data }
                        if (node["id"] != null && node["data"] != null)
                            AddRefEntry(node);
                    }
                }
            }

            // ===== 3) CONFIGURATION (roles) =====
            var confJson = JToken.Parse(File.ReadAllText(configurationPath));
            _configuration = confJson;
            var rolesToken = confJson.SelectToken("$.config.roles") ?? confJson.SelectToken("$.roles");
            if (rolesToken is JArray rolesArr)
            {
                foreach (var r in rolesArr)
                {
                    var label = r?.ToString();
                    if (!string.IsNullOrWhiteSpace(label)) _roles.Add(label!);
                }
            }
        }

        // ---- Public API ----

        public JObject? GetAttributeById(int id) =>
            _attributesById.TryGetValue(id, out var x) ? x : null;

        /// <summary>
        /// Devuelve (label, value) para un referenceId. 
        /// value: intenta ISO-2 en reference.codiAlf2; fallback -> key.
        /// </summary>
        public IEnumerable<(string label, JToken value)> GetReferenceValues(string referenceId)
        {
            if (_referenceById.TryGetValue(referenceId, out var arr))
            {
                foreach (var it in arr.OfType<JObject>())
                {
                    var label = it.Value<string>("value") ?? "";
                    var valueToken = ExtractPreferredValue(it); // ISO-2 si existe, si no key
                    yield return (label, valueToken);
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

        // ---- Helpers ----

        /// <summary>
        /// Preferimos reference.codiAlf2 (ej. "AF"); fallback -> key (ej. "00001").
        /// </summary>
        private static JToken ExtractPreferredValue(JObject item)
        {
            var refObj = item["reference"] as JObject;
            var codiAlf2 = refObj?["codiAlf2"];
            if (codiAlf2 is not null && codiAlf2.Type != JTokenType.Null && codiAlf2.Type != JTokenType.Undefined)
            {
                var s = codiAlf2.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return codiAlf2;
            }

            return item["key"] ?? JValue.CreateNull();
        }

        public JObject? FindConfigAttributeByName(string attributeName)
        {
            if (_configuration is null) return null;

            return _configuration
                .SelectTokens("$.nodes[*].config.forms[*].attributes[*]")
                .OfType<JObject>()
                .FirstOrDefault(a =>
                    string.Equals(
                        a.Value<string>("name"),
                        attributeName,
                        StringComparison.OrdinalIgnoreCase
                    ));
        }


    }
}
