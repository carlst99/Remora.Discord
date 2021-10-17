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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using Remora.Discord.Voice.Abstractions;
using Remora.Discord.Voice.Abstractions.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.UdpDataProtocol;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Errors;
using Remora.Discord.Voice.Interop;
using Remora.Discord.Voice.Interop.Opus;
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
        private readonly DiscordVoiceClientOptions _options;

        private Sodium? _encryptor;
        private SupportedEncryptionMode _encryptionMode;
        private uint _ssrc;
        private ushort _sequence;
        private uint _timestamp;

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
        /// <param name="options">The voice options to use.</param>
        /// <param name="random">A random number generator.</param>
        public UdpVoiceDataTransportService
        (
            IOptions<DiscordVoiceClientOptions> options,
            Random random
        )
        {
            _client = new UdpClient();
            _options = options.Value;
            _encryptionMode = SupportedEncryptionMode.XSalsa20_Poly1305;

            // Randomise as per the RTP specification recommendation.
            _sequence = (ushort)random.Next(0, ushort.MaxValue);
            _timestamp = (uint)random.Next(0, 4096);
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
                    _encryptionMode = SupportedEncryptionModes[mode];
                    return Result<string>.FromSuccess(mode);
                }
            }

            return new VoiceUdpError("None of the encryption modes offered by Discord are supported.");
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
                _ssrc = voiceServerDetails.SSRC;

                return IPDiscoveryResponse.Unpack(discoveryResult.Entity.Buffer);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <inheritdoc />
        public Result Initialize(IReadOnlyList<byte> key)
        {
            if (key.Count != Sodium.KeySize)
            {
                return new ArgumentOutOfRangeError(nameof(key), "Size of key must equal " + Sodium.KeySize);
            }

            byte[] keyBytes = new byte[Sodium.KeySize];
            for (int i = 0; i < Sodium.KeySize; i++)
            {
                keyBytes[i] = key[i];
            }

            Result<Sodium> createEncryptor = Sodium.Create(keyBytes);
            if (!createEncryptor.IsSuccess)
            {
                return Result.FromError(createEncryptor);
            }

            _encryptor = createEncryptor.Entity;

            return Result.FromSuccess();
        }

        /// <inheritdoc />
        public Task<Result> DisconnectAsync(CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public async ValueTask<Result<ReadOnlyMemory<byte>>> ReceiveOpusFrameAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return new InvalidOperationError("The transport service must be connected before frames can be received.");
            }

            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public Result SendFrame(ReadOnlySpan<byte> frame, int pcm16Length)
        {
            try
            {
                if (!IsConnected)
                {
                    return new InvalidOperationError("The transport service must be connected before frames can be sent.");
                }

                const int rtpHeaderSize = 12;
                int encryptedFrameSize = frame.Length + (int)Sodium.MacSize;
                int payloadLength = rtpHeaderSize + encryptedFrameSize; // 12 = RTP Header size
                payloadLength += _encryptionMode switch
                {
                    SupportedEncryptionMode.XSalsa20_Poly1305_Suffix => (int)Sodium.NonceSize,
                    SupportedEncryptionMode.XSalsa20_Poly1305_Lite => sizeof(uint),
                    _ => 0
                };

                Span<byte> payload = stackalloc byte[payloadLength];
                Span<byte> nonce = stackalloc byte[(int)Sodium.NonceSize];

                WriteRtpHeader(payload, pcm16Length);
                WriteNonce(nonce, payload, payload[0..rtpHeaderSize]);

                Result encryptionResult = _encryptor!.Encrypt(frame, payload.Slice(rtpHeaderSize, encryptedFrameSize), nonce);
                if (!encryptionResult.IsSuccess)
                {
                    return encryptionResult;
                }

                _client.Send(payload.ToArray(), payload.Length);

                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Writes an RTP header to the given buffer and updates the internal RTP state.
        /// </summary>
        /// <param name="buffer">The buffer to write the header to.</param>
        /// <param name="pcm16Length">The length of the PCM-16 frame.</param>
        private void WriteRtpHeader(Span<byte> buffer, int pcm16Length)
        {
            buffer[0] = 0x80;
            buffer[1] = 0x78;

            if (_sequence == ushort.MaxValue)
            {
                _sequence = 0;
            }

            BinaryPrimitives.WriteUInt16BigEndian(buffer[2..], _sequence++);
            BinaryPrimitives.WriteUInt32BigEndian(buffer[4..], _timestamp);
            BinaryPrimitives.WriteUInt32BigEndian(buffer[8..], _ssrc);

            _timestamp += (uint)OpusEncoder.CalculateFrameSize(pcm16Length);
        }

        /// <summary>
        /// Generates a nonce and writes it to the packet buffer.
        /// </summary>
        /// <param name="nonceBuffer">The output nonce buffer.</param>
        /// <param name="packetBuffer">The packet buffer.</param>
        /// <param name="rtpHeader">The RTP header, used as the nonce when in <see cref="SupportedEncryptionMode.XSalsa20_Poly1305"/> mode.</param>
        private void WriteNonce(Span<byte> nonceBuffer, Span<byte> packetBuffer, ReadOnlySpan<byte> rtpHeader)
        {
            switch (_encryptionMode)
            {
                case SupportedEncryptionMode.XSalsa20_Poly1305:
                    ZeroFill(nonceBuffer);
                    rtpHeader.CopyTo(nonceBuffer);
                    break;
                case SupportedEncryptionMode.XSalsa20_Poly1305_Suffix:
                    Sodium.GenerateRandomBytes(nonceBuffer);
                    nonceBuffer.CopyTo(packetBuffer[^nonceBuffer.Length..]);
                    break;
                case SupportedEncryptionMode.XSalsa20_Poly1305_Lite:
                    ZeroFill(nonceBuffer);
                    BinaryPrimitives.WriteUInt32BigEndian(nonceBuffer, _timestamp);
                    nonceBuffer[..sizeof(uint)].CopyTo(packetBuffer[^sizeof(uint)..]);
                    break;
            }
        }

        /// <summary>
        /// Fills a buffer with zeroes.
        /// </summary>
        /// <param name="buff">The buffer to fill.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ZeroFill(Span<byte> buff)
        {
            int zero = 0;
            var i = 0;

            for (; i < buff.Length / 4; i++)
            {
                MemoryMarshal.Write(buff, ref zero);
            }

            var remainder = buff.Length % 4;
            if (remainder == 0)
            {
                return;
            }

            for (; i < buff.Length; i++)
            {
                buff[i] = 0;
            }
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
