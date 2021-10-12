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
using System.Collections.Concurrent;
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
        public async Task<Result> SendPayloadAsync<TPayload>(TPayload payload, CancellationToken ct = default) where TPayload : IVoicePayload
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
                ArrayBufferWriter<byte> bufferWriter = new(MaxCommandSize);
                using Utf8JsonWriter jsonWriter = new(bufferWriter, new JsonWriterOptions { SkipValidation = true });

                JsonSerializer.Serialize(jsonWriter, payload, _jsonOptions);

                ReadOnlyMemory<byte> data = bufferWriter.WrittenMemory;

                if (data.Length > MaxCommandSize)
                {
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

            await using var memoryStream = new MemoryStream(); // TODO: Recyclable memory stream

            IMemoryOwner<byte> buffer = MemoryPool<byte>.Shared.Rent(4096);

            try
            {
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await _clientWebSocket.ReceiveAsync(buffer.Memory, ct).ConfigureAwait(false);

                    if (result.MessageType is WebSocketMessageType.Close)
                    {
                        if (Enum.IsDefined(typeof(VoiceGatewayCloseStatus), (int)_clientWebSocket.CloseStatus!.Value))
                        {
                            return new VoiceGatewayDiscordError((VoiceGatewayCloseStatus)_clientWebSocket.CloseStatus.Value);
                        }

                        return new VoiceGatewayWebSocketError(_clientWebSocket.CloseStatus.Value);
                    }

                    await memoryStream.WriteAsync(buffer.Memory.Slice(0, result.Count), ct).ConfigureAwait(false);
                }
                while (!result.EndOfMessage);

                memoryStream.Seek(0, SeekOrigin.Begin);

                using (StreamReader sr = new(memoryStream, Encoding.UTF8, true, -1, true))
                {
                    string jsonValue = sr.ReadToEnd();
                    Console.WriteLine(jsonValue);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                }

                var payload = await JsonSerializer.DeserializeAsync<IVoicePayload>(memoryStream, _jsonOptions, ct).ConfigureAwait(false);
                if (payload is null)
                {
                    return new NotSupportedError
                    (
                        "The received payload deserialized as a null value."
                    );
                }

                return Result<IVoicePayload>.FromSuccess(payload);
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
        }
    }
}
