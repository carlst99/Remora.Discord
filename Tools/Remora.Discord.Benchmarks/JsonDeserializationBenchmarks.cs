//
//  JsonDeserializationBenchmarks.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) 2017 Jarl Gullberg
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Remora.Discord.Benchmarks.Data;

namespace Remora.Discord.Benchmarks
{
    [MemoryDiagnoser]
    public class JsonDeserializationBenchmarks
    {
        private const int MaxPayloadSize = 4096;

        private readonly MemoryStream _memoryStream;
        private readonly ReadOnlySequence<byte> _sequence;
        private readonly ReadOnlyMemory<byte> _memory;

        public JsonDeserializationBenchmarks()
        {
            List<ReadOnlyMemory<byte>> dataBlocks = new();

            ArrayBufferWriter<byte> writer = new();
            using Utf8JsonWriter jsonWriter = new(writer);
            JsonSerializer.Serialize(jsonWriter, Payload.MoreThan4096());

            int start = 0;
            while (start < writer.WrittenCount)
            {
                if (start + MaxPayloadSize > writer.WrittenCount)
                {
                    dataBlocks.Add(writer.WrittenMemory[start..]);
                }
                else
                {
                    dataBlocks.Add(writer.WrittenMemory.Slice(start, MaxPayloadSize));
                }

                start += MaxPayloadSize;
            }

            _sequence = MemoryListToSequence(dataBlocks, dataBlocks[^1].Length);
            _memory = writer.WrittenMemory;

            _memoryStream = new MemoryStream();
            foreach (ReadOnlyMemory<byte> block in dataBlocks)
            {
                _memoryStream.Write(block.Span);
            }
        }

        [Benchmark]
        public async Task<Payload?> DeserializeStream()
        {
            _memoryStream.Seek(0, SeekOrigin.Begin);
            return await JsonSerializer.DeserializeAsync<Payload>(_memoryStream).ConfigureAwait(false);
        }

        [Benchmark]
        public Payload? DeserializeSequence()
        {
            Utf8JsonReader jsonReader = new(_sequence);
            return JsonSerializer.Deserialize<Payload>(ref jsonReader);
        }

        [Benchmark]
        public Payload? DeserializeMemory()
        {
            return JsonSerializer.Deserialize<Payload>(_memory.Span);
        }

        private static ReadOnlySequence<byte> MemoryListToSequence(IList<ReadOnlyMemory<byte>> memorySegments, int endIndex)
        {
            MemorySegment<byte> first = new(memorySegments[0]);

            MemorySegment<byte> current = first;
            for (int i = 1; i < memorySegments.Count; i++)
            {
                current = current.Append(memorySegments[i]);
            }

            return new ReadOnlySequence<byte>(first, 0, current, endIndex);
        }
    }
}
