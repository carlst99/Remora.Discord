//
//  PayloadDeserializationBenchmarks.cs
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
using Microsoft.IO;
using Remora.Discord.Benchmarks.Data;

namespace Remora.Discord.Benchmarks
{
    [MemoryDiagnoser]
    public class PayloadDeserializationBenchmarks
    {
        private const int MaxPayloadSize = 4096;
        private static readonly RecyclableMemoryStreamManager _memoryStreamManager = new();

        private readonly List<ReadOnlyMemory<byte>> _dataBlocks;

        public PayloadDeserializationBenchmarks()
        {
            _dataBlocks = new List<ReadOnlyMemory<byte>>();

            ArrayBufferWriter<byte> writer = new();
            using Utf8JsonWriter jsonWriter = new(writer);
            JsonSerializer.Serialize(jsonWriter, Payload.MoreThan4096());

            int start = 0;
            while (start < writer.WrittenCount)
            {
                if (start + MaxPayloadSize > writer.WrittenCount)
                {
                    _dataBlocks.Add(writer.WrittenMemory.Slice(start));
                }
                else
                {
                    _dataBlocks.Add(writer.WrittenMemory.Slice(start, MaxPayloadSize));
                }

                start += MaxPayloadSize;
            }
        }

        [Benchmark(Baseline = true)]
        public async Task<Payload?> Current()
        {
            await using var memoryStream = new MemoryStream();

            var buffer = ArrayPool<byte>.Shared.Rent(MaxPayloadSize);

            for (int i = 0; i < _dataBlocks.Count; i++)
            {
                _dataBlocks[i].CopyTo(buffer);
                await memoryStream.WriteAsync(buffer, 0, _dataBlocks[i].Length);
            }

            memoryStream.Seek(0, SeekOrigin.Begin);

            var payload = await JsonSerializer.DeserializeAsync<Payload>(memoryStream);
            ArrayPool<byte>.Shared.Return(buffer);

            return payload;
        }

        [Benchmark]
        public async Task<Payload?> CurrentWithRecyclableMemoryStream()
        {
            await using var memoryStream = _memoryStreamManager.GetStream();

            var buffer = ArrayPool<byte>.Shared.Rent(MaxPayloadSize);

            for (int i = 0; i < _dataBlocks.Count; i++)
            {
                _dataBlocks[i].CopyTo(buffer);
                await memoryStream.WriteAsync(buffer, 0, _dataBlocks[i].Length);
            }

            memoryStream.Seek(0, SeekOrigin.Begin);

            var payload = await JsonSerializer.DeserializeAsync<Payload>(memoryStream);
            ArrayPool<byte>.Shared.Return(buffer);

            return payload;
        }

        [Benchmark]
        public Task<Payload?> NewWithSequences()
        {
            List<IMemoryOwner<byte>> dataMemorySegments = new();

            for (int i = 0; i < _dataBlocks.Count; i++)
            {
                IMemoryOwner<byte> segmentBuffer = MemoryPool<byte>.Shared.Rent(MaxPayloadSize);
                _dataBlocks[i].CopyTo(segmentBuffer.Memory);
                dataMemorySegments.Add(segmentBuffer);
            }

            ReadOnlySequence<byte> buffer = MemoryListToSequence(dataMemorySegments, _dataBlocks[^1].Length);

            Payload? p = DeserializeBufferToPayload(buffer);

            foreach (IMemoryOwner<byte> memoryOwner in dataMemorySegments)
            {
                memoryOwner.Dispose();
            }

            dataMemorySegments.Clear();

            return Task.FromResult(p);
        }

        private static ReadOnlySequence<byte> MemoryListToSequence(IList<IMemoryOwner<byte>> memorySegments, int endIndex)
        {
            MemorySegment<byte> first = new(memorySegments[0].Memory);

            MemorySegment<byte> current = first;
            for (int i = 1; i < memorySegments.Count; i++)
            {
                current = current.Append(memorySegments[i].Memory);
            }

            return new ReadOnlySequence<byte>(first, 0, current, endIndex);
        }

        private static Payload? DeserializeBufferToPayload(ReadOnlySequence<byte> buffer)
        {
            Utf8JsonReader jsonReader = new(buffer);
            return JsonSerializer.Deserialize<Payload>(ref jsonReader);
        }
    }

    /// <summary>
    /// Represents a concrete implementation of a <see cref="ReadOnlySequenceSegment{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of data represented by the segment.</typeparam>
#pragma warning disable SA1402 // File may only contain a single type
    internal class MemorySegment<T> : ReadOnlySequenceSegment<T>
#pragma warning restore SA1402 // File may only contain a single type
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MemorySegment{T}"/> class.
        /// </summary>
        /// <param name="memory">The memory instance backing the segment.</param>
        public MemorySegment(ReadOnlyMemory<T> memory)
        {
            Memory = memory;
        }

        /// <summary>
        /// Appends a segment to the current instance.
        /// </summary>
        /// <param name="memory">The memory instance backing the appended segment.</param>
        /// <returns>The appended segment.</returns>
        public MemorySegment<T> Append(ReadOnlyMemory<T> memory)
        {
            var segment = new MemorySegment<T>(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };

            Next = segment;

            return segment;
        }
    }
}
