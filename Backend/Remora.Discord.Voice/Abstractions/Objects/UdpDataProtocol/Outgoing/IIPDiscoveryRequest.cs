//
//  IIPDiscoveryRequest.cs
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

namespace Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol.Outgoing
{
    /// <summary>
    /// Represents an IP discovery request packet.
    /// </summary>
    public interface IIPDiscoveryRequest : IUdpOutgoingDataPacket
    {
        /// <summary>
        /// Gets the type of the discovery packet.
        /// </summary>
        public IPDiscoveryPacketType Type { get; }

        /// <summary>
        /// Gets the length of the packet.
        /// </summary>
        public ushort Length { get; }

        /// <summary>
        /// Gets the SSRC.
        /// </summary>
        public uint SSRC { get; }
    }
}
