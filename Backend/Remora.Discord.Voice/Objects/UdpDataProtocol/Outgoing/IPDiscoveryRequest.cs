//
//  IPDiscoveryRequest.cs
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
using System.Buffers.Binary;

namespace Remora.Discord.Voice.Objects.UdpDataProtocol.Outgoing
{
    /// <summary>
    /// Represents an IP discovery request packet.
    /// </summary>
    /// <param name="SSRC">The SSRC.</param>
    public record IPDiscoveryRequest
    (
        uint SSRC
    )
    {
        /// <summary>
        /// Gets the type of the discovery packet.
        /// </summary>
        public IPDiscoveryPacketType Type => IPDiscoveryPacketType.Request;

        /// <summary>
        /// Gets the length of the discovery packet, not including the 'type' and 'length' fields.
        /// </summary>
        public ushort Length => 70;

        /// <summary>
        /// Packs the packet.
        /// </summary>
        /// <param name="packTo">The buffer to pack the packet into.</param>
        public void Pack(Span<byte> packTo)
        {
            BinaryPrimitives.WriteUInt16BigEndian(packTo, (ushort)IPDiscoveryPacketType.Request);
            BinaryPrimitives.WriteUInt16BigEndian(packTo[2..], 70);
            BinaryPrimitives.WriteUInt32BigEndian(packTo[4..], SSRC);
        }
    }
}
