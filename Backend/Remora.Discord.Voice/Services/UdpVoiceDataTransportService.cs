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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Remora.Discord.Voice.Abstractions;
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
    [PublicAPI]
    public sealed class UdpVoiceDataTransportService : IVoiceDataTranportService, IDisposable
    {
        private static readonly IReadOnlyDictionary<string, SupportedEncryptionMode> SupportedEncryptionModes;

        private readonly UdpClient _client;

        static UdpVoiceDataTransportService()
        {
            SupportedEncryptionModes = new Dictionary<string, SupportedEncryptionMode>()
            {
                ["xsalsa20_poly1305_lite"] = SupportedEncryptionMode.XSalsa20_Poly1305_Lite,
                ["xsalsa20_poly1305_suffix"] = SupportedEncryptionMode.XSalsa20_Poly1305_Suffix,
                ["xsalsa20_poly1305"] = SupportedEncryptionMode.XSalsa20_Poly1305
            };
        }

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
        public Result<string> SelectSupportedEncryptionMode(IReadOnlyList<string> encryptionModes)
        {
            foreach (string mode in encryptionModes)
            {
                if (SupportedEncryptionModes.ContainsKey(mode))
                {
                    return Result<string>.FromSuccess(mode);
                }
            }

            return new VoiceUdpError("A supported encryption mode was not found.");
        }

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
                    .WithTimeout(TimeSpan.FromMilliseconds(1000), ct)
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
        public ValueTask<Result<ReadOnlyMemory<byte>>> ReceiveOpusFrameAsync(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public ValueTask<Result> SendOpusFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
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
