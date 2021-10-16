﻿//
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
using Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol;
using Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol.Outgoing;

namespace Remora.Discord.Voice.Objects.UdpDataProtocol.Outgoing
{
    /// <inheritdoc cref="IIPDiscoveryRequest"/>
    public record IPDiscoveryRequest
    (
        uint SSRC
    ) : IIPDiscoveryRequest
    {
        /// <inheritdoc />
        public IPDiscoveryPacketType Type => IPDiscoveryPacketType.Request;

        /// <inheritdoc />
        public ushort Length => 70;

        /// <inheritdoc />
        public void Pack(Span<byte> packTo)
        {
            BinaryPrimitives.WriteUInt16BigEndian(packTo, (ushort)IPDiscoveryPacketType.Request);
            BinaryPrimitives.WriteUInt16BigEndian(packTo[2..], 70);
            BinaryPrimitives.WriteUInt32BigEndian(packTo[4..], SSRC);
        }
    }
}