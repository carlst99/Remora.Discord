//
//  MemorySegment.cs
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

using System;
using System.Buffers;

namespace Remora.Discord.Voice.Objects
{
    /// <summary>
    /// Represents a concrete implementation of a <see cref="ReadOnlySequenceSegment{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of data represented by the segment.</typeparam>
    internal class MemorySegment<T> : ReadOnlySequenceSegment<T>
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
