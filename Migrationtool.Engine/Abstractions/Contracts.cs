using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine.Abstractions
{
    public interface IRecordReader
    {
        /// <summary>
        /// Devuelve cada objeto JSON (JObject) del archivo de entrada en streaming.
        /// Soporta array JSON grande o NDJSON.
        /// </summary>
        IAsyncEnumerable<JObject> ReadAsync(Stream input, CancellationToken ct = default);
    }

    public interface IRecordMapper
    {
        /// <summary>
        /// Por ahora, mapeo passthrough (dejar tal cual). Luego añadimos reglas condicionales.
        /// Devuelve null si el registro debe descartarse.
        /// </summary>
        JObject? Map(JObject legacy);
    }

    public interface IRecordWriter
    {
        /// <summary>
        /// Escribe cada JObject como una línea NDJSON.
        /// </summary>
        Task WriteAsync(Stream output, IAsyncEnumerable<JObject> records, CancellationToken ct = default);
    }
}
