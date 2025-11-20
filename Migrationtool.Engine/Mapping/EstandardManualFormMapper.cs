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
            // Validación básica: asegurarnos de que viene de la familia adecuada si quieres
            // pero para este bloque nos vale con construir el form objetivo.

            var form = BuildTargetForm(_catalogs);

            // Envuelve con tu shell estándar (usa tu método existente):
            var root = BuildShellWithForm(new List<JObject>()); // crea shell vacío
                                                                // Inserta nuestro form objetivo dentro:
            root["data"]["configForm"]["form"] = form;
            // Ajusta nombre/description si quieres:
            root["name"] = "Dades de la part interessada";
            root["description"] = "Form generat (parts interessades)";

            return root as JObject;
        }

        // --- CÓDIGOS TARGET (normalizamos valores de selects) ---
        private static readonly (string Label, string Value)[] RolMap =
        {
            ("Infractor", "infractor"),
            ("Denunciant", "denunciant"),
            ("Inspector", "inspector"),
            ("Altres Interessats", "altres"),
        };

        private static readonly (string Label, string Value)[] TipusPersonaMap =
        {
            ("Persona física", "fisica"),
            ("Persona jurídica", "juridica"),
            ("Organisme", "organisme"),
            ("Entitat sense personalitat jurídica", "entitat"),
        };

        // PF (persona física)
        private static readonly (string Label, string Value)[] TipusDocumentPFMap =
        {
            ("Número d'identificació estrangers", "nie"),
            ("Número d'identificació fiscal", "nif"),
            ("Número identificador per comunitaris", "nic"),
            ("Passaport", "passaport"),
            ("Provisional", "provisional"),
            ("Targeta d'identitat d'estrangers", "tie"),
            ("No consta", "noconsta"),
            ("Altres", "altres"),
            ("Targeta sanitària individual (TSI)", "tsi"),
        };

        // PJ/ORG/ENT (jurídica/organisme/entitat)
        private static readonly (string Label, string Value)[] TipusDocumentPJMap =
        {
            ("Document d'empresa estrangera", "docEmpresaEstrangera"),
            ("Número d'identificació estrangers", "nie"),
            ("Número d'entitat estrangera", "nee"),
            ("Número d'identificació fiscal", "nif"),
            ("Passaport", "passaport"),
        };

        private static readonly (string Label, string Value)[] GenereMap =
        {
            ("Home", "home"),
            ("Dona", "dona"),
            ("No consta", "noconsta"),
            ("Altres", "altres"),
        };

        private static readonly (string Label, string Value)[] IdiomaMap =
        {
            ("Català", "cat"),
            ("Castellà", "cas"),
            ("Aranès", "aranes"),
            ("Anglès", "eng"),
        };

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

        private static JArray ToValuesArray(IEnumerable<(string Label, string Value)> pairs)
        {
            var arr = new JArray();
            foreach (var (l, v) in pairs)
                arr.Add(new JObject { ["label"] = l, ["value"] = v });
            return arr;
        }

        private static JArray FromReferenceDataOrFallback(
            CatalogStore catalogs,
            string? referenceId,
            IEnumerable<(string Label, string Value)> fallback,
            Func<string, string>? valueNormalizer = null)
        {
            // Intenta leer del catálogo; si no, usa fallback.
            if (!string.IsNullOrWhiteSpace(referenceId) &&
                catalogs.TryGetReferenceValues(referenceId, out var refVals))
            {
                var arr = new JArray();
                foreach (var (label, rawValue) in refVals)
                {
                    var valString = rawValue?.ToString() ?? label;
                    if (valueNormalizer is not null) valString = valueNormalizer(valString);
                    arr.Add(new JObject { ["label"] = label, ["value"] = valString });
                }
                return arr;
            }

            // Fallback local
            return ToValuesArray(fallback);
        }

        private JObject? BuildFieldFromConfigAttribute(string attributeName)
        {
            // Leemos el atributo de configuration.json
            var cfgAttr = _catalogs.FindConfigAttributeByName(attributeName);
            if (cfgAttr == null) return null;

            var label = cfgAttr.Value<string>("label") ?? attributeName;
            var component = cfgAttr.Value<string>("component");
            var attributeId = cfgAttr.Value<int?>("attributeId");

            // Mapear tipo legacy -> Form.io
            var type = component?.ToLowerInvariant() switch
            {
                "textarea" => "textarea",
                "input" => "textfield",
                "select" => "select",
                "boolean" => "checkbox",
                _ => "textfield"
            };

            var field = new JObject
            {
                ["label"] = label,
                ["key"] = attributeName,
                ["type"] = type,
                ["input"] = true,
                ["tableView"] = true
            };

            if (attributeId is int id)
            {
                // Guardamos attributeId por si lo necesitáis luego
                field["properties"] = new JObject { ["attributeId"] = id };

                // Usamos el catálogo de attributes para required + referenceData
                var attrMeta = _catalogs.GetAttributeById(id);
                var validations = attrMeta?["validations"] as JObject;
                if (validations?["required"]?.Value<bool>() == true)
                    field["validate"] = new JObject { ["required"] = true };

                var refId = attrMeta?.Value<string>("referenceData");
                if (!string.IsNullOrWhiteSpace(refId) &&
                    _catalogs.TryGetReferenceValues(refId!, out var refVals))
                {
                    var values = new JArray();
                    foreach (var (lbl, val) in refVals)
                        values.Add(new JObject { ["label"] = lbl, ["value"] = val });

                    field["type"] = "select";
                    field["data"] = new JObject { ["values"] = values };
                    field["searchEnabled"] = values.Count > 15;
                }
            }

            // Caso especial: textarea Observacions
            if (string.Equals(component, "textarea", StringComparison.OrdinalIgnoreCase))
            {
                field["autoExpand"] = true;
                field["showCharCount"] = true;

                var validate = field["validate"] as JObject ?? new JObject();
                validate["maxLength"] = 1000;
                field["validate"] = validate;
            }

            return field;
        }


        private JObject BuildTargetForm(CatalogStore catalogs)
        {
            // 1) TOP ROW (rol, reincident, esTambePersonaDenunciant, tipusPersona)
            var roles = _catalogs.GetRoles(); // desde configuration.config.roles (strings)
                                              // mapeamos a valores normalizados (infractor/denunciant/...)
            var rolValues = new JArray();
            foreach (var r in roles)
            {
                var mapped = RolMap.FirstOrDefault(x =>
                    string.Equals(x.Label, r, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(mapped.Value))
                    rolValues.Add(new JObject { ["label"] = mapped.Label, ["value"] = mapped.Value });
                else
                    rolValues.Add(new JObject { ["label"] = r, ["value"] = r.ToLowerInvariant() });
            }

            if (rolValues.Count == 0)
            {
                // Fallback hardcoded si no hay roles en configuration
                foreach (var (l, v) in RolMap)
                    rolValues.Add(new JObject { ["label"] = l, ["value"] = v });
            }

            // Tipus persona desde catálogo 'tipusPersona' si existe; normalizamos a fisica/juridica/...
            var tipusPersonaValues = FromReferenceDataOrFallback(
                catalogs,
                "tipusPersona",
                TipusPersonaMap,
                valueNormalizer: v =>
                {
                    return v switch
                    {
                        "1" => "fisica",
                        "2" => "juridica",
                        "3" => "organisme",
                        "4" => "entitat",
                        _ => v
                    };
                }
            );

            var rowTop = new JObject
            {
                ["type"] = "columns",
                ["key"] = "rowTop",
                ["columns"] = new JArray
        {
            new JObject // Rol
            {
                ["width"] = 3,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = "Rol",
                        ["key"] = "rol",
                        ["type"] = "select",
                        ["input"] = true,
                        ["validate"] = new JObject { ["required"] = true },
                        ["data"] = new JObject { ["values"] = rolValues },
                        ["searchEnabled"] = false
                    }
                }
            },
            new JObject // Reincident
            {
                ["width"] = 3,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = "Reincident",
                        ["key"] = "reincident",
                        ["type"] = "checkbox",
                        ["input"] = true,
                        ["hidden"] = true,
                        ["customConditional"] = "show = !!data.rol;"
                    }
                }
            },
            new JObject // Es també persona denunciant
            {
                ["width"] = 3,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = "És també persona denunciant",
                        ["key"] = "esTambePersonaDenunciant",
                        ["type"] = "checkbox",
                        ["input"] = true,
                        ["hidden"] = true,
                        ["customConditional"] = "show = !!data.rol;"
                    }
                }
            },
            new JObject // Tipus persona
            {
                ["width"] = 3,
                ["components"] = new JArray
                {
                    new JObject
                    {
                        ["label"] = "Tipus persona",
                        ["key"] = "tipusPersona",
                        ["type"] = "select",
                        ["input"] = true,
                        ["validate"] = new JObject { ["required"] = true },
                        ["data"] = new JObject { ["values"] = tipusPersonaValues },
                        ["searchEnabled"] = false
                    }
                }
            }
        }
            };

            // 2) Persona física
            var tipusDocumentPFValues = FromReferenceDataOrFallback(
                catalogs, /*referenceId*/ "tipusDocumentPF", TipusDocumentPFMap);

            var genereValues = FromReferenceDataOrFallback(
                catalogs, "genere", GenereMap);

            var idiomaValues = FromReferenceDataOrFallback(
                catalogs, "idiomaComunicacio", IdiomaMap);

            var paisValues = FromReferenceDataOrFallback(
                catalogs, "paisosIso", Array.Empty<(string Label, string Value)>());

            var panelPF = new JObject
            {
                ["type"] = "panel",
                ["title"] = "Persona física",
                ["key"] = "panelPersonaFisica",
                ["conditional"] = new JObject { ["show"] = true, ["when"] = "tipusPersona", ["eq"] = "fisica" },
                ["components"] = new JArray
        {
            // Nom - Primer cognom
            new JObject
            {
                ["type"] = "columns",
                ["columns"] = new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Nom", ["key"]="nom", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]=true }
                        }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Primer cognom", ["key"]="primerCognom", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]=true }
                        }
                    }}
                }
            },
            // Segon cognom - Tipus document
            new JObject
            {
                ["type"] = "columns",
                ["columns"] = new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Segon cognom", ["key"]="segonCognom", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Tipus document", ["key"]="tipusDocumentPF", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]=true },
                            ["data"]= new JObject{ ["values"]= tipusDocumentPFValues },
                            ["searchEnabled"]= false
                        }
                    }}
                }
            },
            // Num ident - País doc - Gènere
            new JObject
            {
                ["type"] = "columns",
                ["columns"] = new JArray
                {
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Núm. identificació", ["key"]="numIdentificacioPF", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]=true },
                            ["hidden"]= true,
                            ["customConditional"]= "show = !!data.tipusDocumentPF;"
                        }
                    }},
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{
                            ["label"]="País document", ["key"]="paisDocumentPF", ["type"]="select", ["input"]=true,
                            ["data"]= new JObject{ ["values"]= paisValues },
                            ["placeholder"]="Selecciona país",
                            ["searchEnabled"]= true
                        }
                    }},
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Gènere", ["key"]="genere", ["type"]="select", ["input"]=true,
                            ["data"]= new JObject{ ["values"]= genereValues },
                            ["searchEnabled"]= false
                        }
                    }}
                }
            },
            // Obligació proc - Preferència
            new JObject
            {
                ["type"]="columns",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Obligació electrònica del procés", ["key"]="obligacioElectProcess", ["type"]="checkbox", ["input"]=true, ["disabled"]= true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Preferència/obligació electrònica de la persona", ["key"]="preferenciaElect", ["type"]="checkbox", ["input"]=true }
                    }}
                }
            },
            // Vol email - Email
            new JObject
            {
                ["type"]="columns",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre E-mail", ["key"]="volEmailPF", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=9, ["components"]= new JArray{
                        new JObject{ ["label"]="E-mail", ["key"]="emailPF", ["type"]="email", ["input"]=true }
                    }}
                }
            },
            // Telèfon - Vol SMS - Idioma
            new JObject
            {
                ["type"]="columns",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Telèfon", ["key"]="telefonPF", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre SMS", ["key"]="volSmsPF", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Idioma", ["key"]="idiomaPF", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= idiomaValues },
                            ["searchEnabled"]= false
                        }
                    }}
                }
            }
        }
            };

            // 3) Persona jurídica
            var tipusDocumentPJValues = FromReferenceDataOrFallback(
                catalogs, "tipusDocumentPJ", TipusDocumentPJMap);

            var panelPJ = new JObject
            {
                ["type"] = "panel",
                ["title"] = "Persona jurídica",
                ["key"] = "panelPersonaJuridica",
                ["conditional"] = new JObject { ["show"] = true, ["when"] = "tipusPersona", ["eq"] = "juridica" },
                ["components"] = new JArray
        {
            new JObject // row1
            {
                ["type"]="columns", ["key"]="pj_row1",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Raó social", ["key"]="raoSocialPJ", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true } }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Establiment", ["key"]="establimentPJ", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            new JObject // row2
            {
                ["type"]="columns", ["key"]="pj_row2",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Tipus document", ["key"]="tipusDocumentPJ", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= tipusDocumentPJValues },
                            ["searchEnabled"]= false
                        }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Núm. identificació", ["key"]="numIdentificacioPJ", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true } }
                    }}
                }
            },
            new JObject // row3
            {
                ["type"]="columns", ["key"]="pj_row3",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="E-mail", ["key"]="emailPJ", ["type"]="email", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre E-mail", ["key"]="volEmailPJ", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Telèfon", ["key"]="telefonPJ", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            new JObject // row4
            {
                ["type"]="columns", ["key"]="pj_row4",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre SMS", ["key"]="volSmsPJ", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Idioma", ["key"]="idiomaPJ", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= idiomaValues },
                            ["searchEnabled"]= false
                        }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Disposa de Representant jurídic", ["key"]="teRepresentantPJ", ["type"]="checkbox", ["input"]=true }
                    }}
                }
            }
        }
            };

            // 4) Organisme
            var panelORG = new JObject
            {
                ["type"] = "panel",
                ["title"] = "Organisme",
                ["key"] = "panelOrganisme",
                ["conditional"] = new JObject { ["show"] = true, ["when"] = "tipusPersona", ["eq"] = "organisme" },
                ["components"] = new JArray
        {
            new JObject
            {
                ["type"]="columns", ["key"]="org_row0",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray() },
                    new JObject{ ["width"]=3, ["components"]= new JArray() },
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Organisme EACAT", ["key"]="organismeEacat", ["type"]="checkbox", ["input"]=true }
                    }}
                }
            },
            new JObject // row1
            {
                ["type"]="columns", ["key"]="org_row1",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Raó social", ["key"]="raoSocialORG", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true } }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Establiment", ["key"]="establimentORG", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            new JObject // row2
            {
                ["type"]="columns", ["key"]="org_row2",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Tipus document", ["key"]="tipusDocumentORG", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= tipusDocumentPJValues },
                            ["searchEnabled"]= false
                        }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Núm. identificació", ["key"]="numIdentificacioORG", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true } }
                    }}
                }
            },
            new JObject // row3
            {
                ["type"]="columns", ["key"]="org_row3",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{
                            ["label"]="País document", ["key"]="paisDocumentORG", ["type"]="select", ["input"]=true,
                            ["placeholder"]="Selecciona país",
                            ["data"]= new JObject{ ["values"]= paisValues },
                            ["searchEnabled"]= true
                        }
                    }},
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{ ["label"]="Codi INE10", ["key"]="codiINE10", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{ ["label"]="E-mail", ["key"]="emailORG", ["type"]="email", ["input"]=true }
                    }}
                }
            },
            new JObject // row4
            {
                ["type"]="columns", ["key"]="org_row4",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre E-mail", ["key"]="volEmailORG", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Telèfon", ["key"]="telefonORG", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Idioma", ["key"]="idiomaORG", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= idiomaValues },
                            ["searchEnabled"]= false
                        }
                    }}
                }
            },
            new JObject // row5
            {
                ["type"]="columns", ["key"]="org_row5",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=4, ["components"]= new JArray{
                        new JObject{ ["label"]="Disposa de Representant jurídic", ["key"]="teRepresentantORG", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=8, ["components"]= new JArray() }
                }
            }
        }
            };

            // 5) Entitat sense personalitat jurídica
            var panelENT = new JObject
            {
                ["type"] = "panel",
                ["title"] = "Entitat sense personalitat jurídica",
                ["key"] = "panelEntitat",
                ["conditional"] = new JObject { ["show"] = true, ["when"] = "tipusPersona", ["eq"] = "entitat" },
                ["components"] = new JArray
        {
            new JObject // row1
            {
                ["type"]="columns", ["key"]="ent_row1",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Establiment", ["key"]="establimentENT", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Raó social", ["key"]="raoSocialENT", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true } }
                    }}
                }
            },
            new JObject // row2
            {
                ["type"]="columns", ["key"]="ent_row2",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray() },
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Tipus document", ["key"]="tipusDocumentENT", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= tipusDocumentPJValues },
                            ["searchEnabled"]= false
                        }
                    }}
                }
            },
            new JObject // row3
            {
                ["type"]="columns", ["key"]="ent_row3",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Núm. identificació", ["key"]="numIdentificacioENT", ["type"]="textfield", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true } }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="País document", ["key"]="paisDocumentENT", ["type"]="select", ["input"]=true,
                            ["data"]= new JObject{ ["values"]= paisValues },
                            ["placeholder"]="Selecciona país",
                            ["searchEnabled"]= true
                        }
                    }}
                }
            },
            new JObject // row4
            {
                ["type"]="columns", ["key"]="ent_row4",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="E-mail", ["key"]="emailENT", ["type"]="email", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre E-mail", ["key"]="volEmailENT", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Telèfon", ["key"]="telefonENT", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            new JObject // row5
            {
                ["type"]="columns", ["key"]="ent_row5",
                ["columns"]= new JArray
                {
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Vol rebre SMS", ["key"]="volSmsENT", ["type"]="checkbox", ["input"]=true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Idioma", ["key"]="idiomaENT", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= idiomaValues },
                            ["searchEnabled"]= false
                        }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Disposa de Representant jurídic", ["key"]="teRepresentantENT", ["type"]="checkbox", ["input"]=true }
                    }}
                }
            }
        }
            };

            var panelAdreca = BuildAdrecaPanel(catalogs);

            // Campo Observacions leído desde configuration.json
            var observacionsField = BuildFieldFromConfigAttribute("observacionsAdreca");

            JObject? rowObservacions = null;
            if (observacionsField != null)
            {
                rowObservacions = new JObject
                {
                    ["type"] = "columns",
                    ["columns"] = new JArray
        {
            new JObject
            {
                ["width"] = 12,
                ["components"] = new JArray { observacionsField }
            }
        }
                };
            }


            // 6) Ensamblar los components del FORM
            var components = new JArray { rowTop, panelPF, panelPJ, panelORG, panelENT, panelAdreca };
            if (rowObservacions != null)
            {
                // Lo añadimos justo después del módulo Adreça
                components.Add(rowObservacions);
            }

            var form = new JObject
            {
                ["display"] = "form",
                ["components"] = components
            };


            return form;
        }

        private JObject BuildAdrecaPanel(CatalogStore catalogs)
        {
            // Catálogos
            var tipusViaValues = FromReferenceDataOrFallback(catalogs, "tipusVia", Array.Empty<(string Label, string Value)>());
            var paisValues = FromReferenceDataOrFallback(catalogs, "paisosIso", Array.Empty<(string Label, string Value)>());
            var municipiValues = FromReferenceDataOrFallback(catalogs, "municipis", Array.Empty<(string Label, string Value)>());
            var comarcaValues = FromReferenceDataOrFallback(catalogs, "comarques", Array.Empty<(string Label, string Value)>());
            var provinciaValues = FromReferenceDataOrFallback(catalogs, "provincies", Array.Empty<(string Label, string Value)>());
            var codiPostalValues = FromReferenceDataOrFallback(catalogs, "codisPostals", Array.Empty<(string Label, string Value)>());

            return new JObject
            {
                ["type"] = "panel",
                ["title"] = "Adreça",
                ["key"] = "adrecaPanel",
                ["description"] = "Adreça part interessada",
                ["components"] = new JArray
        {
            // Row A: Tipus de via (3) + vacío (9)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Tipus de via", ["key"]="tipusVia", ["type"]="select", ["input"]=true,
                            ["data"]= new JObject{ ["values"]= tipusViaValues },
                            ["searchEnabled"]= true, ["placeholder"]="Selecciona tipus de via"
                        }
                    }},
                    new JObject{ ["width"]=9, ["components"]= new JArray() }
                }
            },
            // Row B: Nom de via (12)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=12, ["components"]= new JArray{
                        new JObject{ ["label"]="Nom de via", ["key"]="nomVia", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            // Row C: Número via/PK (3) - Polígon industrial (6) - Nau (3)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Número via / PK", ["key"]="numeroVia", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=6, ["components"]= new JArray{
                        new JObject{ ["label"]="Polígon industrial", ["key"]="poligonIndustrial", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Nau", ["key"]="nau", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            // Row D: Bloc (3) - Escala (3) - Pis (3) - Porta (3)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Bloc", ["key"]="bloc", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Escala", ["key"]="escala", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Pis", ["key"]="pis", ["type"]="textfield", ["input"]=true }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{ ["label"]="Porta", ["key"]="porta", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            // Row E: País (3) - Municipi (3) - Comarca* (3) - Província* (3)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="País", ["key"]="pais", ["type"]="select", ["input"]=true,
                            ["data"]= new JObject{ ["values"]= paisValues },
                            ["searchEnabled"]= true, ["placeholder"]="Selecciona país"
                        }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Municipi", ["key"]="municipi", ["type"]="select", ["input"]=true,
                            ["data"]= new JObject{ ["values"]= municipiValues },
                            ["searchEnabled"]= true, ["placeholder"]="Selecciona municipi",
                            ["properties"]= new JObject{ ["dependsOn"]= new JArray("pais") }
                        }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Comarca", ["key"]="comarca", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= comarcaValues },
                            ["searchEnabled"]= true, ["placeholder"]="Selecciona comarca",
                            ["properties"]= new JObject{ ["dependsOn"]= new JArray("municipi") }
                        }
                    }},
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Província", ["key"]="provincia", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= provinciaValues },
                            ["searchEnabled"]= true, ["placeholder"]="Selecciona província",
                            ["properties"]= new JObject{ ["dependsOn"]= new JArray("municipi","comarca") }
                        }
                    }}
                }
            },
            // Row F: Codi Postal* (3) + vacío (9)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=3, ["components"]= new JArray{
                        new JObject{
                            ["label"]="Codi Postal", ["key"]="codiPostal", ["type"]="select", ["input"]=true,
                            ["validate"]= new JObject{ ["required"]= true },
                            ["data"]= new JObject{ ["values"]= codiPostalValues },
                            ["searchEnabled"]= true,
                            ["properties"]= new JObject{ ["dependsOn"]= new JArray("municipi","comarca","provincia") }
                        }
                    }},
                    new JObject{ ["width"]=9, ["components"]= new JArray() }
                }
            },
            // Row G: Dades complementàries adreça (12)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=12, ["components"]= new JArray{
                        new JObject{ ["label"]="Dades complementàries adreça", ["key"]="comentarisAdreca", ["type"]="textfield", ["input"]=true }
                    }}
                }
            },
            // Row H: Dades complementàries caràtula impressió (12)
            new JObject{
                ["type"]="columns",
                ["columns"]= new JArray{
                    new JObject{ ["width"]=12, ["components"]= new JArray{
                        new JObject{ ["label"]="Dades complementàries caràtula impressió", ["key"]="observacionsCaratula", ["type"]="textfield", ["input"]=true }
                    }}
                }
            }
        }
            };
        }

    }
}
