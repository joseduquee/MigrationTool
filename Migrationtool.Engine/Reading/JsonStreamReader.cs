using MigrationTool.Engine.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine.Reading
{
    public sealed class JsonStreamReader : IRecordReader
    {
        public async IAsyncEnumerable<JObject> ReadAsync(Stream input, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            using var sr = new StreamReader(input);
            using var jr = new JsonTextReader(sr)
            {
                CloseInput = false,
                SupportMultipleContent = true,   // permite NDJSON
                // Comentarios: false por defecto
                // DateParseHandling = DateParseHandling.None (por si prefieres no parsear fechas automáticamente)
            };

            // Detectamos si el primer token es un array o si vienen objetos sueltos (NDJSON/multicontent)
            // 1) Si es array: iteramos cada objeto dentro del array sin cargar todo en memoria.
            // 2) Si no: asumimos NDJSON/objetos sueltos.
            if (!await jr.ReadAsync(ct)) yield break;

            if (jr.TokenType == JsonToken.StartArray)
            {
                while (await jr.ReadAsync(ct))
                {
                    if (jr.TokenType == JsonToken.StartObject)
                    {
                        var obj = await JObject.LoadAsync(jr, ct);
                        yield return obj;
                    }
                    else if (jr.TokenType == JsonToken.EndArray)
                    {
                        break;
                    }
                }
            }
            else
            {
                // Retrocede una posición lógica: jr ya leyó algo que no es StartArray.
                // SupportMultipleContent permite varios objetos JSON en el mismo stream (NDJSON).
                do
                {
                    if (jr.TokenType == JsonToken.StartObject)
                    {
                        var obj = await JObject.LoadAsync(jr, ct);
                        yield return obj;
                    }
                }
                while (await jr.ReadAsync(ct));
            }
        }
    }
}
