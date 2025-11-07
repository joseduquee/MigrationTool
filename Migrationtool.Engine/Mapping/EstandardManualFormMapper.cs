using System;
using System.Collections.Generic;
using System.Linq;
using MigrationTool.Engine.Abstractions;
using MigrationTool.Engine.Catalogs;
using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine.Mapping
{
    /// <summary>
    /// Genera un documento Form.io (en data.configForm.form.components)
    /// a partir de legacy: nodes[].config.forms[].attributes[] usando catálogos + configuration.
    /// </summary>
    public sealed class EstandardManualFormMapper : IRecordMapper
    {
        private readonly CatalogStore _catalogs;

        public EstandardManualFormMapper(CatalogStore catalogs)
        {
            _catalogs = catalogs;
        }

        public JObject? Map(JObject legacy)
        {
            // 1) Localizar el bloque de formulario dentro de nodes[*].config.forms[*]
            var forms = legacy.SelectTokens("$.nodes[*].config.forms[*]").OfType<JObject>().ToList();
            if (forms.Count == 0)
                return BuildShellWithEmptyForm("No forms array under any node's config.forms");

            // Encuentra el 'node' al que pertenece cada form usando Ancestors()
            JObject? formBlock = null;

            foreach (var f in forms)
            {
                // Sube hasta el JObject 'node' que tiene { name, type, config, ... }
                var node = f.Ancestors()
                            .OfType<JObject>()
                            .FirstOrDefault(o =>
                                o["name"] != null &&
                                o["config"] is JObject cfg &&
                                cfg["forms"] != null);

                var nodeName = node?["name"]?.ToString() ?? "";
                var cfg = node?["config"] as JObject;
                var subType = cfg?["activitySubtype"]?.ToString()
                             ?? cfg?["acivitySubtype"]?.ToString(); // legacy typo a veces

                // Criterios de selección:
                // - subtype == partsInteressades
                // - o el name contiene partsInteressades (como tu ejemplo FEM_partsInteressades_gestioPartsInteressades)
                if (string.Equals(subType, "partsInteressades", StringComparison.OrdinalIgnoreCase)
                    || nodeName.IndexOf("partsInteressades", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    formBlock = f;
                    // Diagnóstico opcional
                    Console.WriteLine($"[FOUND] node='{nodeName}', activitySubtype='{subType}'");
                    break;
                }
            }


            if (formBlock is null)
                return BuildShellWithEmptyForm("No form found for partsInteressades");

            var attributes = formBlock["attributes"] as JArray ?? new JArray();
            // 2) Traducir attributes → componentes Form.io
            var rootComponents = new List<JObject>();
            var panelsByKey = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

            foreach (var a in attributes.OfType<JObject>())
            {
                var attributeId = a.Value<int?>("attributeId");
                var name = a.Value<string>("name") ?? "";
                var label = a.Value<string>("label") ?? name;
                var component = a.Value<string>("component") ?? "input"; // legacy UI hint
                var disabled = a.Value<bool?>("disabled") ?? false;

                // Enriquecer desde attributes_catalog
                var cat = (attributeId is int aid) ? _catalogs.GetAttributeById(aid) : null;
                var validations = (cat?["validations"] as JArray)?.Select(x => x.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
                var referenceData = cat?["referenceData"]?.ToString();
                var dependencies = cat?["dependencies"] as JObject;

                // Construir el componente Form.io
                var formioCmp = BuildComponent(name, label, component, disabled, validations, referenceData, attributeId);

                // Visibilidad / disabled desde dependencies
                ApplyDependencies(formioCmp, dependencies);

                // Paneles: nombres con prefijo "xxx$campo"
                if (name.Contains('$'))
                {
                    var parts = name.Split('$', 2);
                    var groupKey = parts[0];     // representant | adreca | ...
                    var childKey = parts[1];

                    // el campo dentro del container debe llevar el key "sufijo"
                    formioCmp["key"] = childKey;

                    if (!panelsByKey.TryGetValue(groupKey, out var container))
                    {
                        container = NewContainer(groupKey, ToTitle(groupKey));
                        // Regla especial: mostrar "representant" solo si isRepresentant = true
                        if (string.Equals(groupKey, "representant", StringComparison.OrdinalIgnoreCase))
                        {
                            container["customConditional"] = "show = !!data.isRepresentant;";
                        }
                        panelsByKey[groupKey] = container;
                        rootComponents.Add(container);
                    }

                    var comps = (container["components"] as JArray)!;
                    comps.Add(formioCmp);
                }
                else if (string.Equals(component, "section", StringComparison.OrdinalIgnoreCase))
                {
                    // La "section" legacy define el contenedor (p.ej. adreca, representant)
                    if (!panelsByKey.ContainsKey(name))
                    {
                        var container = NewContainer(name, label);
                        if (string.Equals(name, "representant", StringComparison.OrdinalIgnoreCase))
                        {
                            container["customConditional"] = "show = !!data.isRepresentant;";
                        }
                        panelsByKey[name] = container;
                        rootComponents.Add(container);
                    }
                }
                else
                {
                    rootComponents.Add(formioCmp);
                }

            }

            // 3) Construir documento destino (shell + form)
            return BuildShellWithForm(rootComponents);
        }

        private static JObject NewPanel(string key, string label) => new JObject
        {
            ["label"] = label,
            ["key"] = key,
            ["type"] = "panel",
            ["input"] = false,
            ["tableView"] = false,
            ["components"] = new JArray()
        };

        private JObject BuildComponent(
            string name, string label, string legacyComponent, bool disabled,
            HashSet<string> validations, string? referenceData, int? attributeId)
        {
            // Decidir tipo destino
            var type = legacyComponent switch
            {
                "select" => "select",
                "boolean" => "checkbox",
                "mask" => "textfield",
                "identityDocument" => "textfield",
                "panel" => "panel",
                "section" => "panel",
                _ => "textfield"
            };

            var cmp = new JObject
            {
                ["label"] = label,
                ["key"] = name,
                ["type"] = type,
                ["input"] = !string.Equals(type, "panel", StringComparison.OrdinalIgnoreCase),
                ["tableView"] = !string.Equals(type, "panel", StringComparison.OrdinalIgnoreCase),
                ["validateWhenHidden"] = false
            };

            if (attributeId is int id)
            {
                cmp["properties"] = new JObject
                {
                    ["attributeId"] = id
                };
            }

            if (disabled) cmp["disabled"] = true;

            // required
            if (validations.Contains("required"))
            {
                cmp["validate"] = new JObject { ["required"] = true };
            }

            // SELECT: desde referenceData o (caso especial) rol desde configuration
            if (string.Equals(type, "select", StringComparison.OrdinalIgnoreCase) ||
                HasReferenceDataForceSelect(name, referenceData))
            {
                var values = new JArray();

                if (!string.IsNullOrEmpty(referenceData) && _catalogs.TryGetReferenceValues(referenceData, out var refVals))
                {
                    foreach (var (labelV, val) in refVals)
                        values.Add(new JObject { ["label"] = labelV, ["value"] = val });
                }
                else if (string.Equals(name, "rol", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var r in _catalogs.GetRoles())
                        values.Add(new JObject { ["label"] = r, ["value"] = r });
                }

                if (values.Count() > 0)
                {
                    cmp["type"] = "select";
                    cmp["data"] = new JObject { ["values"] = values };
                }
            }

            return cmp;
        }

        private static bool HasReferenceDataForceSelect(string name, string? referenceData)
        {
            // Algunos attributes vienen como type=boolean pero traen referenceData → deben ser select (ej. tipusPersona, idioma)
            if (!string.IsNullOrEmpty(referenceData)) return true;
            return false;
        }

        private static void ApplyDependencies(JObject cmp, JObject? dependencies)
        {
            if (dependencies is null) return;

            // VISIBILITY: ej. ["tipusPersona=1|2|3|4"]
            var visibility = dependencies["visibility"] as JArray;
            if (visibility is not null && visibility.Count > 0)
            {
                // Por ahora solo soportamos 1 regla simple A=v1|v2|v3...
                var rule = visibility[0]?.ToString();
                var (field, op, rawVals) = ParseSimpleRule(rule);
                if (field is not null && op == "=" && rawVals?.Length > 0)
                {
                    var js = $"show = [{string.Join(",", rawVals)}].includes(data.{field});";
                    cmp["customConditional"] = js;
                }
            }

            // DISABLED: ej. ["organismeEACAT=false"]
            var disabled = dependencies["disabled"] as JArray;
            if (disabled is not null && disabled.Count > 0)
            {
                var rule = disabled[0]?.ToString();
                if (cmp["properties"] is not JObject props) { props = new JObject(); cmp["properties"] = props; }
                props["disabledRule"] = rule; // guardamos la regla; más adelante podemos traducirla a Form.io "logic"
            }
        }

        private static (string? field, string? op, string[]? values) ParseSimpleRule(string? rule)
        {
            // "tipusPersona=1|2|3|4"  -> field=tipusPersona, op="=", values=["1","2","3","4"]
            if (string.IsNullOrWhiteSpace(rule)) return (null, null, null);
            var opIdx = rule.IndexOf('=');
            if (opIdx < 0) return (null, null, null);
            var field = rule.Substring(0, opIdx).Trim();
            var values = rule.Substring(opIdx + 1).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return (field, "=", values);
        }

        private static string ToTitle(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static JObject BuildShellWithEmptyForm(string reason)
        {
            var root = BuildShellWithForm(new List<JObject>());
            root["__warning"] = reason;
            return root;
        }

        private static JObject BuildShellWithForm(List<JObject> components)
        {
            // Shell mínimo basado en tu ejemplo. Ajusta si tu backend espera otros campos.
            var nowIso = DateTime.UtcNow.ToString("O");

            var root = new JObject
            {
                ["family"] = "CONF",
                ["subfamily"] = "FORMSS",
                ["name"] = "Generat - Estàndard Manual",
                ["description"] = "Migració automàtica des de legacy (parts interessades)",
                ["functionalId"] = "CONF_FORMSS_AUTO",
                ["mode"] = "MANUAL",
                ["externalId"] = null,
                ["objectPath"] = null,
                ["objectPathS3"] = null,
                ["version"] = new JObject { ["isCurrent"] = true, ["major"] = 1, ["minor"] = 0 },
                ["status"] = "INPROGRESS",
                ["state"] = "EnCurs",
                ["scopes"] = new JArray("corporatiu"),
                ["users"] = new JArray(),
                ["tags"] = new JArray("formulari", "migrat"),
                ["isPriority"] = false,
                ["specificMetadata"] = null,
                ["searchSimple"] = null,
                ["search"] = null,
                ["controlData"] = new JObject
                {
                    ["creationUser"] = "migration",
                    ["updateUser"] = "migration",
                    ["creationDate"] = new JObject { ["$date"] = nowIso },
                    ["lastUpdate"] = new JObject { ["$date"] = nowIso }
                },
                ["data"] = new JObject
                {
                    ["configForm"] = new JObject
                    {
                        ["mode"] = "render",
                        ["path"] = "data",
                        ["form"] = new JObject
                        {
                            ["components"] = new JArray(
                                // contenedor columns opcional; si no lo quieres, devuelve components directamente
                                new JObject
                                {
                                    ["label"] = "Dades de la part interessada",
                                    ["type"] = "panel",
                                    ["key"] = "dadesPartInteressada",
                                    ["input"] = false,
                                    ["tableView"] = false,
                                    ["components"] = new JArray(components)
                                }
                            )
                        }
                    }
                }
            };

            return root;
        }

        private static bool IsNumeric(string s) => int.TryParse(s, out _);

        private static string QuoteIfNeeded(string v)
            => IsNumeric(v) ? v : $"\"{v}\"";

        private static JObject NewContainer(string key, string label) => new JObject
        {
            ["label"] = label,
            ["key"] = key,
            ["type"] = "container",
            ["input"] = true,
            ["tableView"] = false,
            ["components"] = new JArray()
        };


    }
}
