using MigrationTool.Engine.Abstractions;
using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine.Mapping
{
    public sealed class PassThroughMapper : IRecordMapper
    {
        public JObject? Map(JObject legacy)
        {
            // Aquí luego aplicaremos reglas condicionales complejas.
            // Por ahora devolvemos tal cual para validar el pipeline y el rendimiento.
            return legacy;
        }
    }
}
