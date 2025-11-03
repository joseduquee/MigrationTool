namespace MigrationTool.Cli
{
    internal sealed class Options
    {
        public string? InputPath { get; init; }
        public string? OutputPath { get; init; }

        public static Options Parse(string[] args)
        {
            string? input = null, output = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.Equals("--input", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    input = args[++i];
                else if (a.Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    output = args[++i];
                else if (a is "-h" or "--help" or "/?")
                    ShowUsageAndExit();
            }

            return new Options { InputPath = input, OutputPath = output };
        }

        public static void ShowUsageAndExit()
        {
            Console.WriteLine("Uso:");
            Console.WriteLine("  MigrationTool.Cli --input <ruta_input.json|.ndjson> --output <ruta_output.ndjson>");
            Console.WriteLine("Si no se pasan parámetros, se usan rutas por defecto en la carpeta 'samples' del proyecto.");
            Environment.Exit(0);
        }
    }
}
