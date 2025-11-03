using MigrationTool.Engine.Abstractions;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace MigrationTool.Engine.Mapping
{
    public sealed class ConditionalMapper : IRecordMapper
    {
        public JObject? Map(JObject legacy)
        {
            // 1) id obligatorio
            var id = legacy.SelectToken("id")?.ToString()?.Trim();
            if (string.IsNullOrEmpty(id))
                return null; // SKIP: sin id no migramos

            // 2) status normalizado
            var rawStatus = legacy.SelectToken("status")?.ToString()?.Trim();
            var status = rawStatus switch
            {
                "OK" => "Confirmed",
                "PEND" => "Pending",
                "CANC" => "Cancelled",
                null or "" => "Unknown",
                _ => "Unknown"
            };

            // 3) fecha: acepta varias rutas/campos y formatos
            var createdAt = NormalizeDate(
                legacy.SelectToken("date")?.ToString()
                ?? legacy.SelectToken("created_at")?.ToString()
                ?? legacy.SelectToken("createdAt")?.ToString()
                ?? legacy.SelectToken("timestamp")?.ToString()
            );

            // 4) total: número o string → decimal; default 0
            var totalAmount = NormalizeDecimal(
                legacy.SelectToken("total")?.ToString()
                ?? legacy.SelectToken("amount")?.ToString()
                ?? legacy.SelectToken("price")?.ToString()
            );

            // 5) construir objeto destino mínimo
            var output = new JObject
            {
                ["id"] = id,
                ["createdAt"] = createdAt,         // string ISO 8601 (o null si no hay forma)
                ["totalAmount"] = totalAmount,     // decimal
                ["status"] = status
            };

            return output;
        }

        private static string? NormalizeDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Intenta varios formatos comunes
            var formats = new[]
            {
                "yyyy-MM-dd",
                "dd/MM/yyyy",
                "O",                // ISO 8601 round-trip
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss",
                "MM/dd/yyyy"
            };

            foreach (var f in formats)
            {
                if (DateTimeOffset.TryParseExact(raw, f, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                    return dto.ToUniversalTime().ToString("O");
            }

            // intento laxo
            if (DateTimeOffset.TryParse(raw, out var any))
                return any.ToUniversalTime().ToString("O");

            return null; // no forzamos error: dejaremos null y mediremos en reportes más adelante
        }

        private static decimal NormalizeDecimal(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0m;

            // Reemplaza coma por punto si viene "1,23"
            var cleaned = raw.Replace(',', '.');

            // Intenta parse decimal invariante
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;

            return 0m;
        }
    }
}
