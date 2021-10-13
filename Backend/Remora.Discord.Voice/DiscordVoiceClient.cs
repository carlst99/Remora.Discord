//
//  DiscordVoiceClient.cs
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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Options;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.Core;
using Remora.Discord.Gateway;
using Remora.Discord.Voice.Abstractions.Objects;
using Remora.Discord.Voice.Abstractions.Objects.Commands;
using Remora.Discord.Voice.Abstractions.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.Events.Heartbeats;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Errors;
using Remora.Discord.Voice.Objects;
using Remora.Discord.Voice.Objects.Commands.ConnectingResuming;
using Remora.Discord.Voice.Objects.Commands.Heartbeats;
using Remora.Results;

namespace Remora.Discord.Voice
{
    /// <summary>
    /// Represents a Discord Voice Gateway client.
    /// </summary>
    [PublicAPI]
    public sealed class DiscordVoiceClient
    {
        private readonly DiscordGatewayClient _gatewayClient;
        private readonly DiscordVoiceClientOptions _clientOptions;
        private readonly IConnectionEstablishmentWaiterService _connectionWaiterService;
        private readonly IVoicePayloadTransportService _transportService;
        private readonly Random _random;

        /// <summary>
        /// Holds data pertaining to the heartbeating process.
        /// </summary>
        private readonly HeartbeatData _heartbeatData;

        /// <summary>
        /// Holds payloads that have been submitted by the application, but have not yet been sent to the gateway.
        /// </summary>
        private readonly ConcurrentQueue<IVoicePayload> _payloadsToSend;

        /// <summary>
        /// Holds payloads that have been received by the gateway, but not yet distributed to the application.
        /// </summary>
        private readonly ConcurrentQueue<IVoicePayload> _receivedPayloads;

        /// <summary>
        /// Holds the cancellation token source for internal operations.
        /// </summary>
        private CancellationTokenSource _disconnectRequestedSource;

        private GatewayConnectionStatus _connectionStatus;
        private VoiceConnectionEstablishmentDetails? _connectionDetails;
        private IVoiceReady? _voiceServerData;

        private Task<Result>? _sendTask;

        /// <summary>
        /// The time a heartbeat acknowledgement was last received, represented using <see cref="DateTimeOffset.Ticks"/>.
        /// </summary>
        private long _lastReceivedHeartbeatAck;

        /// <summary>
        /// The nonce attached to the last received payload.
        /// </summary>
        private int _lastReceivedHeartbeatNonce;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscordVoiceClient"/> class.
        /// </summary>
        /// <param name="gatewayClient">The gateway client.</param>
        /// <param name="clientOptions">The client options to use.</param>
        /// <param name="connectionWaiterService">The connection waiter service.</param>
        /// <param name="transportService">The voice payload transport service.</param>
        /// <param name="random">A random number generator.</param>
        public DiscordVoiceClient
        (
            DiscordGatewayClient gatewayClient,
            IOptions<DiscordVoiceClientOptions> clientOptions,
            IConnectionEstablishmentWaiterService connectionWaiterService,
            IVoicePayloadTransportService transportService,
            Random random
        )
        {
            _gatewayClient = gatewayClient;
            _clientOptions = clientOptions.Value;
            _connectionWaiterService = connectionWaiterService;
            _transportService = transportService;
            _random = random;

            _heartbeatData = new HeartbeatData();
            _payloadsToSend = new ConcurrentQueue<IVoicePayload>();
            _receivedPayloads = new ConcurrentQueue<IVoicePayload>();
            _disconnectRequestedSource = new CancellationTokenSource();

            _connectionStatus = GatewayConnectionStatus.Offline;
        }

        /// <summary>
        /// Starts and connects the voice gateway client.
        /// <para>
        /// This task will not complete until cancelled (or faulted), maintaining the connection for the duration of it.
        ///
        /// If the gateway client encounters a fatal problem during the execution of this task, it will return with a
        /// failed result. If a shutdown is requested, it will gracefully terminate the connection and return a
        /// successful result.
        /// </para>
        /// </summary>
        /// <param name="connectionParameters">The connection parameters to use.</param>
        /// <param name="ct">A token by which the caller can request this method to stop.</param>
        /// <returns>A gateway connection result which may or may not have succeeded.</returns>
        public async Task<Result> RunAsync(UpdateVoiceState connectionParameters, CancellationToken ct)
        {
            try
            {
                if (_connectionStatus is not GatewayConnectionStatus.Offline)
                {
                    return new InvalidOperationError("Already running.");
                }

                while (!ct.IsCancellationRequested)
                {
                    switch (_connectionStatus)
                    {
                        case GatewayConnectionStatus.Offline:
                            Result initialConnectionResult = await InitialConnectionAsync(connectionParameters, ct).ConfigureAwait(false);
                            if (!initialConnectionResult.IsSuccess)
                            {
                                return initialConnectionResult;
                            }

                            // TODO: Setup receive task
                            break;
                        case GatewayConnectionStatus.Disconnected:
                            Result resumeConnectionResult = await ResumeConnectionAsync(ct).ConfigureAwait(false);
                            if (!resumeConnectionResult.IsSuccess)
                            {
                                return resumeConnectionResult;
                            }

                            // TODO: Setup receive task
                            break;
                        case GatewayConnectionStatus.Connected:
                            // TODO: Check send and receive task
                            // TODO: Dispatch received events
                            // TODO: Check health of voice socket
                            break;
                    }
                }

                return Result.FromSuccess();
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return ex;
            }
            finally
            {
                SendDisconnectVoiceStateUpdate(connectionParameters.GuildID);
            }
        }

        /// <summary>
        /// Enqueues a voice gateway command for sending.
        /// </summary>
        /// <typeparam name="TCommand">The type of the command to send.</typeparam>
        /// <param name="command">The command object.</param>
        public void EnqueueCommand<TCommand>(TCommand command) where TCommand : IVoiceGatewayCommand
        {
            VoicePayload<TCommand> payload = new(command);
            _payloadsToSend.Enqueue(payload);
        }

        /// <summary>
        /// Performs the initial handshake logic to connect with the voice gateway.
        /// Calls <see cref="ConnectAndBeginSending(Uri, CancellationToken)"/>.
        /// </summary>
        /// <param name="connectionParameters">The connection parameters.</param>
        /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
        /// <returns>A <see cref="Result"/> representing the outcome of the operation.</returns>
        private async Task<Result> InitialConnectionAsync
        (
            UpdateVoiceState connectionParameters,
            CancellationToken ct
        )
        {
            _gatewayClient.SubmitCommand(connectionParameters);

            Result<VoiceConnectionEstablishmentDetails> getConnectionDetails = await _connectionWaiterService.WaitForRequestConfirmation
            (
                connectionParameters.GuildID,
                ct
            ).ConfigureAwait(false);

            if (!getConnectionDetails.IsDefined())
            {
                return Result.FromError(getConnectionDetails);
            }

            _connectionDetails = getConnectionDetails.Entity;

            // Using the full namespace here to help avoid potential confusion between the normal and voice gateway event sets.
            API.Abstractions.Gateway.Events.IVoiceStateUpdate voiceState = getConnectionDetails.Entity.VoiceState;
            API.Abstractions.Gateway.Events.IVoiceServerUpdate voiceServer = getConnectionDetails.Entity.VoiceServer;

            if (voiceServer.Endpoint is null)
            {
                return new VoiceServerUnavailableError();
            }

            Result<Uri> constructUriResult = ConstructVoiceGatewayEndpoint(voiceServer.Endpoint);
            if (!constructUriResult.IsDefined())
            {
                return Result.FromError(constructUriResult);
            }

            // Connect the websocket and start the send task
            Result webSocketConnectionResult = await ConnectAndBeginSending
            (
                constructUriResult.Entity,
                ct
            ).ConfigureAwait(false);

            if (!webSocketConnectionResult.IsSuccess)
            {
                return webSocketConnectionResult;
            }

            EnqueueCommand
            (
                new VoiceIdentify
                (
                    voiceServer.GuildID,
                    voiceState.UserID,
                    voiceState.SessionID,
                    voiceServer.Token
                )
            );

            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    return new VoiceGatewayError("Operation was cancelled.", true);
                }

                // TODO: Dispatch
                Result<IVoicePayload> readyPayload = await _transportService.ReceivePayloadAsync(ct).ConfigureAwait(false);
                if (!readyPayload.IsDefined())
                {
                    return Result.FromError
                    (
                        new VoiceGatewayError("Failed to receive voice ready payload", true),
                        readyPayload
                    );
                }

                if (readyPayload.Entity is IVoicePayload<IVoiceHeartbeatAcknowledge>)
                {
                    continue;
                }

                if (readyPayload.Entity is not IVoicePayload<IVoiceReady> ready)
                {
                    return new VoiceGatewayError("The identification response payload from the gateway was not a ready message.", true);
                }

                _voiceServerData = ready.Data;
                break;
            }

            _connectionStatus = GatewayConnectionStatus.Connected;
            return Result.FromSuccess();
        }

        /// <summary>
        /// Resumes an existing session with the voice gateway, replaying missed events.
        /// </summary>
        /// <param name="ct">A <see cref="CancellationToken"/>.</param>
        /// <returns>A connection result which may or may not have succeeded.</returns>
        private async Task<Result> ResumeConnectionAsync
        (
            CancellationToken ct
        )
        {
            if (_connectionDetails is null)
            {
                return new InvalidOperationError("There is no session to resume.");
            }

            // Using the full namespace here to help avoid potential confusion between the normal and voice gateway event sets.
            API.Abstractions.Gateway.Events.IVoiceStateUpdate voiceState = _connectionDetails.VoiceState;
            API.Abstractions.Gateway.Events.IVoiceServerUpdate voiceServer = _connectionDetails.VoiceServer;

            Result<Uri> constructUriResult = ConstructVoiceGatewayEndpoint(voiceServer.Endpoint!);
            if (!constructUriResult.IsDefined())
            {
                return Result.FromError(constructUriResult);
            }

            // Connect the websocket and start the send task
            Result webSocketConnectionResult = await ConnectAndBeginSending
            (
                constructUriResult.Entity,
                ct
            ).ConfigureAwait(false);

            if (!webSocketConnectionResult.IsSuccess)
            {
                return webSocketConnectionResult;
            }

            EnqueueCommand
            (
                new VoiceResume
                (
                    voiceServer.GuildID,
                    voiceState.SessionID,
                    voiceServer.Token
                )
            );

            // Push resumed events onto the queue
            var resuming = true;
            while (resuming)
            {
                if (ct.IsCancellationRequested)
                {
                    return new VoiceGatewayError("Operation was cancelled.", true);
                }

                var receiveEvent = await _transportService.ReceivePayloadAsync(ct).ConfigureAwait(false);

                if (!receiveEvent.IsSuccess)
                {
                    if (receiveEvent.Error is VoiceGatewayDiscordError)
                    {
                        // Reconnect on next iteration
                        _connectionStatus = GatewayConnectionStatus.Offline;
                        return Result.FromSuccess();
                    }

                    return Result.FromError
                    (
                        new VoiceGatewayError
                        (
                            "Failed to receive a payload.",
                            true
                        ),
                        receiveEvent
                    );
                }

                switch (receiveEvent.Entity)
                {
                    case IVoicePayload<IVoiceHeartbeatAcknowledge>:
                            continue;
                    case IVoicePayload<IVoiceResumed>:
                            // TODO: Dispatch
                            // UnwrapAndDispatchEvent(receiveEvent.Entity, _disconnectRequestedSource.Token);
                            resuming = false;
                            break;
                }

                _receivedPayloads.Enqueue(receiveEvent.Entity);
            }

            _connectionStatus = GatewayConnectionStatus.Connected;
            return Result.FromSuccess();
        }

        /// <summary>
        /// Constructs a <see cref="Uri"/> to the voice gateway endpoint.
        /// </summary>
        /// <param name="endpoint">The provided string endpoint.</param>
        /// <returns>A <see cref="Result"/> representing the outcome of the operation.</returns>
        private Result<Uri> ConstructVoiceGatewayEndpoint(string endpoint)
        {
            endpoint = $"wss://{endpoint}?v=4";
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var gatewayUri))
            {
                return new VoiceGatewayError
                (
                    "Failed to parse the received voice gateway endpoint.",
                    true
                );
            }

            return gatewayUri;
        }

        /// <summary>
        /// Connects the websocket and begins the send task.
        /// </summary>
        /// <param name="gatewayUri">The URI of the voice gateway.</param>
        /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
        /// <returns>A <see cref="Result"/> indicating the outcome of the operation.</returns>
        private async Task<Result> ConnectAndBeginSending
        (
            Uri gatewayUri,
            CancellationToken ct
        )
        {
            Result connectResult = await _transportService.ConnectAsync(gatewayUri, ct).ConfigureAwait(false);
            if (!connectResult.IsSuccess)
            {
                return connectResult;
            }

            Result<IVoicePayload> helloPayload = await _transportService.ReceivePayloadAsync(ct).ConfigureAwait(false);
            if (!helloPayload.IsDefined())
            {
                return Result.FromError
                (
                    new VoiceGatewayError("Failed to receive hello payload.", true),
                    helloPayload
                );
            }

            if (helloPayload.Entity is not IVoiceHello hello)
            {
                return new VoiceGatewayError("The first payload from the voice gateway was not a hello. Rude!", true);
            }

            _heartbeatData.Interval = hello.HeartbeatInterval;
            _sendTask = GatewaySenderAsync(_disconnectRequestedSource.Token);

            return Result.FromSuccess();
        }

        /// <summary>
        /// This method acts as the main entrypoint for the gateway sender task. It processes payloads that are
        /// submitted by the application to the gateway, sending them to it.
        /// </summary>
        /// <param name="ct">A token for requests to disconnect the socket.</param>
        /// <returns>
        /// A sender result which may or may not have been successful. A failed result indicates that something
        /// has gone wrong when sending a payload, and that the connection has been deemed nonviable. A nonviable
        /// connection should be either terminated, reestablished, or resumed as appropriate.
        /// </returns>
        private async Task<Result> GatewaySenderAsync(CancellationToken ct)
        {
            await Task.Yield();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Result<TimeSpan> heartbeatResult = await SendHeartbeatAsync(ct).ConfigureAwait(false);
                    if (!heartbeatResult.IsSuccess)
                    {
                        return Result.FromError(heartbeatResult);
                    }

                    // Check if there are any user-submitted payloads to send
                    if (!_payloadsToSend.TryDequeue(out var payload))
                    {
                        // Let's sleep for a little while
                        TimeSpan sleepTime = TimeSpan.FromMilliseconds(Math.Clamp(100, 0, heartbeatResult.Entity.TotalMilliseconds));
                        await Task.Delay(sleepTime, ct).ConfigureAwait(false);
                        continue;
                    }

                    var sendResult = await _transportService.SendPayloadAsync(payload, ct).ConfigureAwait(false);
                    if (sendResult.IsSuccess)
                    {
                        continue;
                    }

                    // Normal closures are okay
                    return sendResult.Error is VoiceGatewayWebSocketError { CloseStatus: System.Net.WebSockets.WebSocketCloseStatus.NormalClosure }
                        ? Result.FromSuccess()
                        : sendResult;
                }

                return Result.FromSuccess();
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
                return Result.FromSuccess();
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Sends a heartbeat if required.
        /// </summary>
        /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
        /// <returns>
        /// A <see cref="Result"/> representing the outcome of the operation,
        /// and containing the maximum safe amount of time needed until the next heartbeat if the operation was successful.
        /// </returns>
        private async Task<Result<TimeSpan>> SendHeartbeatAsync(CancellationToken ct)
        {
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                TimeSpan safetyMargin = _clientOptions.GetTrueHeartbeatSafetyMargin(_heartbeatData.Interval);

                if (_heartbeatData.LastSentTime is null || now - _heartbeatData.LastSentTime >= _heartbeatData.Interval - safetyMargin)
                {
                    if (_heartbeatData.LastReceivedAckTime < _heartbeatData.LastSentTime)
                    {
                        return new VoiceGatewayError
                        (
                            "The server did not respond in time with a heartbeat acknowledgement.",
                            false
                        );
                    }

                    if (_heartbeatData.LastSentNonce != _heartbeatData.LastReceivedNonce)
                    {
                        return new VoiceGatewayError
                        (
                            "The server did not respond with a valid heartbeat.",
                            false
                        );
                    }

                    _heartbeatData.LastSentNonce = _random.Next();
                    Result sendHeartbeatResult = await _transportService.SendPayloadAsync
                    (
                        new VoicePayload<VoiceHeartbeat>
                        (
                            new VoiceHeartbeat(_heartbeatData.LastReceivedNonce)
                        ),
                        ct
                    ).ConfigureAwait(false);

                    if (!sendHeartbeatResult.IsSuccess)
                    {
                        return Result<TimeSpan>.FromError
                        (
                            new VoiceGatewayError("Failed to send a heartbeat.", false),
                            sendHeartbeatResult
                        );
                    }

                    _heartbeatData.LastSentTime = DateTimeOffset.UtcNow;
                }

                TimeSpan safeTimeTillNext = _heartbeatData.LastSentTime.Value + _heartbeatData.Interval - safetyMargin - now;
                return Result<TimeSpan>.FromSuccess(safeTimeTillNext);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>
        /// Sends an <see cref="UpdateVoiceState"/> command to the gateway, requesting that the bot be disconnected from a guild's voice channel.
        /// </summary>
        /// <param name="guildID">The guild containing the voice channel to disconnect from.</param>
        private void SendDisconnectVoiceStateUpdate(Snowflake guildID)
        {
            _gatewayClient.SubmitCommand
            (
                new UpdateVoiceState
                (
                    guildID,
                    false,
                    false
                )
            );
        }
    }
}
