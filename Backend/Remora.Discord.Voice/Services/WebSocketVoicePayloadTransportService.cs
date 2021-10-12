//
//  WebSocketVoicePayloadTransportService.cs
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
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Remora.Discord.Voice.Abstractions.Objects;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Errors;
using Remora.Results;

namespace Remora.Discord.Voice.Services
{
    /// <summary>
    /// Represents a websocket-based transport service.
    /// </summary>
    [PublicAPI]
    public sealed class WebSocketVoicePayloadTransportService : IVoicePayloadTransportService, IAsyncDisposable
    {
        /// <summary>
        /// Gets the maximum size in bytes that a command may be.
        /// </summary>
        private const int MaxCommandSize = 4096;

        private readonly IServiceProvider _services;
        private readonly JsonSerializerOptions _jsonOptions;

        private readonly SemaphoreSlim _payloadSendSemaphore;
        private readonly Utf8JsonWriter _payloadJsonWriter;

        private readonly SemaphoreSlim _payloadReceiveSemaphore;
        private readonly Pipe _payloadReceivePipe;

        private ArrayBufferWriter<byte> _payloadSendBuffer;

        /// <summary>
        /// Holds the currently available websocket client.
        /// </summary>
        private ClientWebSocket? _clientWebSocket;

        /// <inheritdoc />
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketVoicePayloadTransportService"/> class.
        /// </summary>
        /// <param name="services">The services available to the application.</param>
        /// <param name="jsonOptions">The JSON options.</param>
        public WebSocketVoicePayloadTransportService
        (
            IServiceProvider services,
            IOptions<JsonSerializerOptions> jsonOptions
        )
        {
            _services = services;
            _jsonOptions = jsonOptions.Value;

            _payloadSendSemaphore = new SemaphoreSlim(1, 1);
            _payloadSendBuffer = new ArrayBufferWriter<byte>(MaxCommandSize);
            _payloadJsonWriter = new Utf8JsonWriter
            (
                _payloadSendBuffer,
                new JsonWriterOptions { SkipValidation = true } // The JSON Serializer should handle everything correctly
            );

            _payloadReceiveSemaphore = new SemaphoreSlim(1, 1);
            _payloadReceivePipe = new Pipe
            (
                new PipeOptions(minimumSegmentSize: MaxCommandSize)
            );
        }

        /// <inheritdoc />
        public async Task<Result> ConnectAsync(Uri endpoint, CancellationToken ct = default)
        {
            if (_clientWebSocket is not null)
            {
                return new InvalidOperationError("The transport service is already connected.");
            }

            var socket = _services.GetRequiredService<ClientWebSocket>();

            try
            {
                await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
                switch (socket.State)
                {
                    case WebSocketState.Open:
                    case WebSocketState.Connecting:
                        {
                            break;
                        }
                    default:
                        {
                            socket.Dispose();
                            return new Gateway.Results.WebSocketError
                            (
                                socket.State,
                                "Failed to connect to the endpoint."
                            );
                        }
                }
            }
            catch (Exception e)
            {
                socket.Dispose();
                return e;
            }

            _clientWebSocket = socket;

            this.IsConnected = true;
            return Result.FromSuccess();
        }

        /// <inheritdoc />
        public async ValueTask<Result> SendPayloadAsync<TPayload>(TPayload payload, CancellationToken ct = default) where TPayload : IVoicePayload
        {
            if (_clientWebSocket is null)
            {
                return new InvalidOperationError("The transport service is not connected.");
            }

            if (_clientWebSocket.State is not WebSocketState.Open)
            {
                return new InvalidOperationError("The socket is not open.");
            }

            try
            {
                JsonSerializer.Serialize(_payloadJsonWriter, payload, _jsonOptions);

                ReadOnlyMemory<byte> data = _payloadSendBuffer.WrittenMemory;

                if (data.Length > MaxCommandSize)
                {
                    // Reset the backing buffer so we don't hold on to more memory than necessary
                    _payloadSendBuffer = new ArrayBufferWriter<byte>(MaxCommandSize);
                    _payloadJsonWriter.Reset(_payloadSendBuffer);

                    return new NotSupportedError("The payload was too large to be accepted by the gateway.");
                }

                bool entered = await _payloadSendSemaphore.WaitAsync(1000, ct).ConfigureAwait(false);
                if (!entered)
                {
                    return new OperationCanceledException("Could not enter semaphore.");
                }

                await _clientWebSocket.SendAsync(data, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ex;
            }
            finally
            {
                _payloadSendSemaphore.Release();
                _payloadSendBuffer.Clear();
                _payloadJsonWriter.Reset();
            }

            return Result.FromSuccess();
        }

        /// <inheritdoc />
        public async Task<Result<IVoicePayload>> ReceivePayloadAsync(CancellationToken ct = default)
        {
            if (_clientWebSocket is null)
            {
                return new InvalidOperationError("The transport service is not connected.");
            }

            if (_clientWebSocket.State != WebSocketState.Open)
            {
                return new InvalidOperationError("The socket was not open.");
            }

            try
            {
                ValueWebSocketReceiveResult socketReceiveResult;

                do
                {
                    Memory<byte> segmentBuffer = _payloadReceivePipe.Writer.GetMemory(MaxCommandSize);
                    socketReceiveResult = await _clientWebSocket.ReceiveAsync(segmentBuffer, ct).ConfigureAwait(false);

                    if (socketReceiveResult.MessageType is WebSocketMessageType.Close)
                    {
                        if (Enum.IsDefined(typeof(VoiceGatewayCloseStatus), (int)_clientWebSocket.CloseStatus!.Value))
                        {
                            return new VoiceGatewayDiscordError((VoiceGatewayCloseStatus)_clientWebSocket.CloseStatus.Value);
                        }

                        return new VoiceGatewayWebSocketError(_clientWebSocket.CloseStatus.Value);
                    }

                    _payloadReceivePipe.Writer.Advance(socketReceiveResult.Count);
                }
                while (!socketReceiveResult.EndOfMessage);

                FlushResult flushResult = await _payloadReceivePipe.Writer.FlushAsync(ct).ConfigureAwait(false);
                if (flushResult.IsCanceled)
                {
                    return new TaskCanceledException();
                }

                ReadResult readResult = await _payloadReceivePipe.Reader.ReadAsync(ct).ConfigureAwait(false);

                Result<IVoicePayload?> getPayload = DeserializeBufferToPayload(readResult.Buffer, socketReceiveResult.Count);
                if (!getPayload.IsSuccess)
                {
                    return Result<IVoicePayload>.FromError(getPayload);
                }

                if (getPayload.Entity is null)
                {
                    return new NotSupportedError
                    (
                        "The received payload deserialized as a null value."
                    );
                }

                _payloadReceivePipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

                return Result<IVoicePayload>.FromSuccess(getPayload.Entity);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Deserializes a voice payload object from a byte buffer.
        /// </summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="payloadEndInBuffer">The position into the buffer at which the payload data ends.</param>
        /// <returns>A result representing the deserialized object.</returns>
        private Result<IVoicePayload?> DeserializeBufferToPayload(ReadOnlySequence<byte> buffer, int payloadEndInBuffer)
        {
            try
            {
                Utf8JsonReader jsonReader = new(buffer.Slice(0, payloadEndInBuffer));
                return Result<IVoicePayload?>.FromSuccess(JsonSerializer.Deserialize<IVoicePayload>(ref jsonReader, _jsonOptions));
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <inheritdoc/>
        public async Task<Result> DisconnectAsync
        (
            bool reconnectionIntended,
            CancellationToken ct = default
        )
        {
            if (_clientWebSocket is null)
            {
                return new InvalidOperationError("The transport service is not connected.");
            }

            switch (_clientWebSocket.State)
            {
                case WebSocketState.Open:
                case WebSocketState.CloseReceived:
                case WebSocketState.CloseSent:
                    {
                        try
                        {
                            // 1012 is used here instead of normal closure, because close codes 1000 and 1001 don't
                            // allow for reconnection. 1012 is referenced in the websocket protocol as "Service restart",
                            // which makes sense for our use case.
                            var closeCode = reconnectionIntended
                                ? (WebSocketCloseStatus)1012
                                : WebSocketCloseStatus.NormalClosure;

                            await _clientWebSocket.CloseAsync
                            (
                                closeCode,
                                "Terminating connection by user request.",
                                ct
                            ).ConfigureAwait(false);
                        }
                        catch (WebSocketException)
                        {
                            // Most likely due to some kind of premature or forced disconnection; we'll live with it
                        }
                        catch (OperationCanceledException)
                        {
                            // We still need to cleanup the socket
                        }

                        break;
                    }
            }

            _clientWebSocket.Dispose();
            _clientWebSocket = null;

            _payloadReceivePipe.Reader.Complete();
            _payloadReceivePipe.Writer.Complete();

            this.IsConnected = false;
            return Result.FromSuccess();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);

            if (_clientWebSocket is null)
            {
                return;
            }

            await DisconnectAsync(false).ConfigureAwait(false);

            await _payloadJsonWriter.DisposeAsync().ConfigureAwait(false);
            _payloadSendSemaphore.Dispose();

            _payloadReceiveSemaphore.Dispose();
        }
    }
}
