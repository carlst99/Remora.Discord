//
//  PayloadSerializationBenchmarks.cs
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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Remora.Discord.Benchmarks.Data;

namespace Remora.Discord.Benchmarks
{
    [MemoryDiagnoser]
    public class PayloadSerializationBenchmarks
    {
        private readonly Payload _payload;

        private readonly SemaphoreSlim _payloadSendSemaphore;
        private readonly ArrayBufferWriter<byte> _payloadSendBuffer;
        private readonly Utf8JsonWriter _payloadJsonWriter;

        public PayloadSerializationBenchmarks()
        {
            _payload = new Payload();

            _payloadSendSemaphore = new SemaphoreSlim(1, 1);
            _payloadSendBuffer = new ArrayBufferWriter<byte>(4096);
            _payloadJsonWriter = new Utf8JsonWriter
            (
                _payloadSendBuffer,
                new JsonWriterOptions { SkipValidation = true } // The JSON Serializer should handle everything correctly
            );
        }

        [Benchmark(Baseline = true)]
        public async Task<ArraySegment<byte>> Old()
        {
            await using var memoryStream = new MemoryStream();

            byte[]? buffer = null;
            try
            {
                await JsonSerializer.SerializeAsync(memoryStream, _payload);

                buffer = ArrayPool<byte>.Shared.Rent((int)memoryStream.Length);
                memoryStream.Seek(0, SeekOrigin.Begin);

                // Copy the data
                var bufferSegment = new ArraySegment<byte>(buffer, 0, (int)memoryStream.Length);
                await memoryStream.ReadAsync(bufferSegment);

                return bufferSegment;
            }
            finally
            {
                if (buffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> New()
        {
            JsonSerializer.Serialize(_payloadJsonWriter, _payload);

            ReadOnlyMemory<byte> data = _payloadSendBuffer.WrittenMemory;

            bool entered = await _payloadSendSemaphore.WaitAsync(1000).ConfigureAwait(false);
            if (!entered)
            {
                return data;
            }

            _payloadSendSemaphore.Release();
            _payloadSendBuffer.Clear();
            _payloadJsonWriter.Reset();

            // Normally the data is already written away, so it doesn't matter that we're returning a clear memory segment in the benchmark.
            return data;
        }
    }
}
