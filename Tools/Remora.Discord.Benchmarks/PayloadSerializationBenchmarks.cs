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
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using Remora.Discord.Benchmarks.Data;

namespace Remora.Discord.Benchmarks
{
    [MemoryDiagnoser]
    public class PayloadSerializationBenchmarks
    {
        private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new();

        private readonly Payload _payload;
        private readonly ArrayBufferWriter<byte> _payloadSendBuffer;
        private readonly Utf8JsonWriter _payloadJsonWriter;

        private long _length;

        public PayloadSerializationBenchmarks()
        {
            _payload = Payload.LessThan4096();

            _payloadSendBuffer = new ArrayBufferWriter<byte>(4096);
            _payloadJsonWriter = new Utf8JsonWriter
            (
                _payloadSendBuffer,
                new JsonWriterOptions { SkipValidation = true } // The JSON Serializer should handle everything correctly
            );
        }

        [GlobalCleanup]
        public void After()
        {
            Console.WriteLine(_length);
        }

        [Benchmark(Baseline = true)]
        public async Task Current()
        {
            await using var memoryStream = new MemoryStream();

            byte[]? buffer = null;
            try
            {
                await JsonSerializer.SerializeAsync(memoryStream, _payload);

                buffer = ArrayPool<byte>.Shared.Rent((int)memoryStream.Length);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var bufferSegment = new ArraySegment<byte>(buffer, 0, (int)memoryStream.Length);
                await memoryStream.ReadAsync(bufferSegment);

                Send(bufferSegment.AsMemory());
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
        public async Task NewWithRecyclableMemoryStream()
        {
            await using var memoryStream = _memoryStreamManager.GetStream();
            await JsonSerializer.SerializeAsync(memoryStream, _payload);

            Send(memoryStream.GetBuffer().AsMemory(0, (int)memoryStream.Position));
        }

        [Benchmark]
        public Task New()
        {
            JsonSerializer.Serialize(_payloadJsonWriter, _payload);

            ReadOnlyMemory<byte> data = _payloadSendBuffer.WrittenMemory;
            Send(data);

            _payloadSendBuffer.Clear();
            _payloadJsonWriter.Reset();

            return Task.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Send(ReadOnlyMemory<byte> buffer)
        {
            unchecked
            {
                // An attempt to ensure this method and buffer pass isn't trimmed
                _length += buffer.Length;
            }
        }
    }
}
