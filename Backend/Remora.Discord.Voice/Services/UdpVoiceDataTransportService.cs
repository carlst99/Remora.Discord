//
//  UdpVoiceDataTransportService.cs
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
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Remora.Discord.Voice.Abstractions.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol;
using Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol.Incoming;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Errors;
using Remora.Discord.Voice.Objects.UdpDataProtocol.Incoming;
using Remora.Discord.Voice.Objects.UdpDataProtocol.Outgoing;
using Remora.Results;

namespace Remora.Discord.Voice.Services
{
    /// <summary>
    /// Represents a UDP-based transport service for voice data.
    /// </summary>
    public sealed class UdpVoiceDataTransportService : IVoiceDataTranportService, IDisposable
    {
        private readonly UdpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpVoiceDataTransportService"/> class.
        /// </summary>
        public UdpVoiceDataTransportService()
        {
            _client = new UdpClient();
        }

        /// <inheritdoc />
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Gets a value indicating whether or not this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public async Task<Result<IIPDiscoveryResponse>> ConnectAsync(IVoiceReady voiceServerDetails, CancellationToken ct = default)
        {
            try
            {
                _client.Connect(voiceServerDetails.IP, voiceServerDetails.Port);

#pragma warning disable SA1305 // Field names should not use Hungarian notation

                // This should only happen once every while, so we're not concerned about renting from a pool.
                byte[] ipDiscoveryBuffer = new byte[74];

#pragma warning restore SA1305 // Field names should not use Hungarian notation

                new IPDiscoveryRequest(voiceServerDetails.SSRC).Pack(ipDiscoveryBuffer);
                await _client.SendAsync(ipDiscoveryBuffer, ipDiscoveryBuffer.Length).ConfigureAwait(false);

                Result<UdpReceiveResult> discoveryResult = await _client.ReceiveAsync()
                    .WithTimeout(TimeSpan.FromMilliseconds(1000))
                    .ConfigureAwait(false);

                if (!discoveryResult.IsSuccess)
                {
                    return new VoiceUdpError("Timed out while waiting to receive an IP discovery packet.");
                }

                ushort packetType = BinaryPrimitives.ReadUInt16BigEndian(discoveryResult.Entity.Buffer);

                if (packetType != (ushort)IPDiscoveryPacketType.Response)
                {
                    return new VoiceUdpError("Failed to receive an IP discovery response");
                }

                IsConnected = true;
                return IPDiscoveryResponse.Unpack(discoveryResult.Entity.Buffer);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <inheritdoc />
        public Task<Result> DisconnectAsync(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ValueTask<Result<ReadOnlyMemory<byte>>> ReceivePayloadAsync(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ValueTask<Result> SendPayloadAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    _client.Dispose();
                }

                IsDisposed = true;
            }
        }
    }
}
