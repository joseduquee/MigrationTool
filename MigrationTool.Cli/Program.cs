using MigrationTool.Cli;
using MigrationTool.Engine;
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
            Directory.CreateDirectory(samplesDir);

            var defaultInput = Path.Combine(samplesDir, "input.json");
            var defaultOutput = Path.Combine(samplesDir, "output.ndjson");

            var inputPath = opts.InputPath ?? defaultInput;
            var outputPath = opts.OutputPath ?? defaultOutput;

            Console.WriteLine($"Input : {inputPath}");
            Console.WriteLine($"Output: {outputPath}");

            if (!File.Exists(inputPath))
            {
                // Crea ejemplo NDJSON si no hay input
                await File.WriteAllTextAsync(inputPath, "{ \"id\": 1, \"status\": \"OK\", \"date\": \"2025-11-01\" }\n{ \"id\": 2, \"status\": \"PEND\", \"date\": \"01/11/2025\" }\n");
                Console.WriteLine("[INFO] No se encontró input.json. He creado uno de ejemplo.");
            }

            // PRECREAR output para que siempre aparezca en VS
            await File.WriteAllTextAsync(outputPath, "");

            var orchestrator = new MigrationOrchestrator(
                reader: new JsonStreamReader(),
                mapper: new ConditionalMapper(), // ← usamos el nuevo mapper condicional
                writer: new NdjsonWriter()
            );

            var start = DateTime.UtcNow;
            var report = await orchestrator.RunAsync(inputPath, outputPath, CancellationToken.None);
            var elapsed = DateTime.UtcNow - start;

            Console.WriteLine($"\n=== REPORT ===");
            Console.WriteLine($"OK      : {report.Ok}");
            Console.WriteLine($"SKIPPED : {report.Skipped}");
            Console.WriteLine($"FAIL    : {report.Fail}");
            Console.WriteLine($"Elapsed : {elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"Output  : {outputPath}");

            return report.Fail > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
