//
//  JsonSerializationBenchmarks.cs
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

using System.Buffers;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using Remora.Discord.Benchmarks.Data;

namespace Remora.Discord.Benchmarks
{
    [MemoryDiagnoser]
    public class JsonSerializationBenchmarks
    {
        private const int MaxPayloadSize = 4096;

        private readonly Payload _payload;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly ArrayBufferWriter<byte> _payloadSendBuffer;
        private readonly Utf8JsonWriter _payloadJsonWriter;

        public JsonSerializationBenchmarks()
        {
            _payload = Payload.LessThan4096();
            _memoryStreamManager = new RecyclableMemoryStreamManager();
            _payloadSendBuffer = new ArrayBufferWriter<byte>(MaxPayloadSize);
            _payloadJsonWriter = new Utf8JsonWriter
            (
                _payloadSendBuffer,
                new JsonWriterOptions { SkipValidation = true } // The JSON Serializer should handle everything correctly
            );
        }

        [Benchmark]
        public async Task SerializeStream()
        {
            await using MemoryStream ms = _memoryStreamManager.GetStream();
            await JsonSerializer.SerializeAsync(ms, _payload).ConfigureAwait(false);
        }

        [Benchmark]
        public bool SerializeSequence()
        {
            JsonSerializer.Serialize(_payloadJsonWriter, _payload);
            _payloadSendBuffer.Clear();
            _payloadJsonWriter.Reset();

            return true;
        }
    }
}
