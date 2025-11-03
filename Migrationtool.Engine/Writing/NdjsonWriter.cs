using MigrationTool.Engine.Abstractions;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MigrationTool.Engine.Writing
{
    public sealed class NdjsonWriter : IRecordWriter
    {
        public async Task WriteAsync(Stream output, IAsyncEnumerable<JObject> records, CancellationToken ct = default)
        {
            // Escribimos una línea por objeto
            await using var sw = new StreamWriter(output, new UTF8Encoding(false), bufferSize: 1 << 16, leaveOpen: true);

            await foreach (var obj in records.WithCancellation(ct))
            {
                await sw.WriteLineAsync(obj.ToString(Newtonsoft.Json.Formatting.None));
            }
            await sw.FlushAsync();
        }
    }
}
