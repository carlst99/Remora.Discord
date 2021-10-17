//
//  IPDiscoveryResponse.cs
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
using System.Text;
using Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol;

namespace Remora.Discord.Voice.Objects.UdpDataProtocol.Incoming
{
    /// <inheritdoc cref=" IIPDiscoveryResponse"/>
    public record IPDiscoveryResponse
    (
        IPDiscoveryPacketType Type,
        ushort Length,
        uint SSRC,
        string Address,
        ushort Port
    ) : IIPDiscoveryResponse
    {
        /// <summary>
        /// Unpacks from raw data.
        /// </summary>
        /// <param name="unpackFrom">The data to unpack from.</param>
        /// <returns>An <see cref="IPDiscoveryResponse"/> object.</returns>
        public static IPDiscoveryResponse Unpack(ReadOnlySpan<byte> unpackFrom)
        {
            IPDiscoveryPacketType type = (IPDiscoveryPacketType)BinaryPrimitives.ReadUInt16BigEndian(unpackFrom);
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(unpackFrom[2..]);
            uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(unpackFrom[4..]);
            string address = Encoding.UTF8.GetString(unpackFrom[8..72]);
            ushort port = BinaryPrimitives.ReadUInt16BigEndian(unpackFrom[72..]);

            return new IPDiscoveryResponse
            (
                type,
                length,
                ssrc,
                address,
                port
            );
        }
    }
}
