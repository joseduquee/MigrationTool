using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Engine.Abstractions;
using Newtonsoft.Json.Linq;

namespace MigrationTool.Engine
{
    public sealed class MigrationReport
    {
        public int Ok { get; init; }
        public int Skipped { get; init; }
        public int Fail { get; init; }
    }

    public sealed class MigrationOrchestrator
    {
        private readonly IRecordReader _reader;
        private readonly IRecordMapper _mapper;
        private readonly IRecordWriter _writer;

        public MigrationOrchestrator(IRecordReader reader, IRecordMapper mapper, IRecordWriter writer)
        {
            _reader = reader;
            _mapper = mapper;
            _writer = writer;
        }

        public async Task<MigrationReport> RunAsync(string inputPath, string outputPath, CancellationToken ct = default)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found", inputPath);

            await using var input = File.OpenRead(inputPath);
            await using var output = File.Create(outputPath);

            int ok = 0, skipped = 0, fail = 0;

            async IAsyncEnumerable<JObject> Project([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
            {
                await foreach (var item in _reader.ReadAsync(input, token).WithCancellation(token))
                {
                    JObject? mapped = null;
                    try
                    {
                        mapped = _mapper.Map(item);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[MAP_ERROR] {ex.Message}");
                        fail++;
                    }

                    if (mapped is null)
                    {
                        skipped++;
                        continue;
                    }

                    ok++;
                    yield return mapped;
                }
            }

            await _writer.WriteAsync(output, Project(ct), ct);

            return new MigrationReport { Ok = ok, Skipped = skipped, Fail = fail };
        }
    }
}
