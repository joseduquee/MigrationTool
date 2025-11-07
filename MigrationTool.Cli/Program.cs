using MigrationTool.Cli;
using MigrationTool.Engine;
using MigrationTool.Engine.Catalogs;
using MigrationTool.Engine.Mapping;
using MigrationTool.Engine.Reading;
using MigrationTool.Engine.Writing;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            var opts = Options.Parse(args);

            var baseDir = AppContext.BaseDirectory;                         // ...\bin\Debug\net8.0\
            var projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            var samplesDir = Path.Combine(projectDir, "samples");             // dentro del proyecto CLI
            var catalogsDir = Path.Combine(samplesDir, "catalogs");
            Directory.CreateDirectory(samplesDir);
            Directory.CreateDirectory(catalogsDir);

            var defaultInput = Path.Combine(samplesDir, "configuration.json");
            var defaultOutput = Path.Combine(samplesDir, "output.ndjson");

            var inputPath = opts.InputPath ?? defaultInput;
            var outputPath = opts.OutputPath ?? defaultOutput;

            // Rutas de catálogos (marca estos archivos como Content + Copy if newer)
            var attributesCatalogPath = Path.Combine(catalogsDir, "attributes_catalog.json");
            var referenceDataCatalogPath = Path.Combine(catalogsDir, "reference_data_catalog.json");
            var configurationPath = defaultInput;

            Console.WriteLine($"[PATH] projectDir = {projectDir}");
            Console.WriteLine($"Input  : {inputPath}");
            Console.WriteLine($"Output : {outputPath}");
            Console.WriteLine($"Catalogs:");
            Console.WriteLine($"  attributes_catalog      = {attributesCatalogPath}");
            Console.WriteLine($"  reference_data_catalog  = {referenceDataCatalogPath}");
            Console.WriteLine($"  configuration           = {configurationPath}");

            if (!File.Exists(inputPath))
            {
                await File.WriteAllTextAsync(inputPath, /* NDJSON minimal */
                    "{ \"nodes\": [ { \"name\":\"FEM_partsInteressades_gestioPartsInteressades\", \"config\": { \"activitySubtype\":\"partsInteressades\", \"forms\":[ { \"attributes\":[ {\"attributeId\":577,\"name\":\"rol\",\"label\":\"Rol\",\"component\":\"select\"}, {\"attributeId\":580,\"name\":\"tipusPersona\",\"label\":\"Tipus persona\",\"component\":\"select\"}, {\"attributeId\":623,\"name\":\"idioma\",\"label\":\"Idioma\",\"component\":\"select\"} ] } ] } } ] }\n"
                );
                Console.WriteLine("[INFO] input.json no existía: creado uno de ejemplo.");
            }

            // Precrear output (0 bytes) para que sea visible en VS siempre
            await File.WriteAllTextAsync(outputPath, "");
            Console.WriteLine("[DIAG] Output pre-creado (0 bytes).");

            // Cargar catálogos (fallará si faltan archivos: así detectas pronto el problema)
            var catalogs = new CatalogStore(attributesCatalogPath, referenceDataCatalogPath, configurationPath);

            // DEBUG: contadores de carga
            Console.WriteLine($"[CAT] attributes loaded:    {System.IO.File.Exists(attributesCatalogPath)}");
            Console.WriteLine($"[CAT] reference loaded:     {System.IO.File.Exists(referenceDataCatalogPath)}");
            Console.WriteLine($"[CAT] roles (configuration): {string.Join(", ", catalogs.GetRoles())}");

            // Orquestador con el mapper "estándar manual" → Form.io
            var orchestrator = new MigrationOrchestrator(
                reader: new JsonStreamReader(),
                mapper: new EstandardManualFormMapper(catalogs),
                writer: new NdjsonWriter()
            );

            var start = DateTime.UtcNow;
            var report = await orchestrator.RunAsync(inputPath, outputPath, CancellationToken.None);
            var elapsed = DateTime.UtcNow - start;

            Console.WriteLine("\n=== REPORT ===");
            Console.WriteLine($"OK      : {report.Ok}");
            Console.WriteLine($"SKIPPED : {report.Skipped}");
            Console.WriteLine($"FAIL    : {report.Fail}");
            Console.WriteLine($"Elapsed : {elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"Output  : {outputPath}");

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{outputPath}\"",
                    UseShellExecute = true
                });
            }
            catch { /* no crítico en CI */ }

            return report.Fail > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
