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
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.Core;
using Remora.Discord.Gateway;
using Remora.Discord.Gateway.Results;
using Remora.Discord.Voice.Abstractions.Objects;
using Remora.Discord.Voice.Abstractions.Objects.Commands;
using Remora.Discord.Voice.Abstractions.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.Events.Heartbeats;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Errors;
using Remora.Discord.Voice.Objects;
using Remora.Discord.Voice.Objects.Commands.ConnectingResuming;
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
        private readonly IConnectionEstablishmentWaiterService _connectionWaiterService;
        private readonly IVoicePayloadTransportService _transportService;

        private GatewayConnectionStatus _connectionStatus;

        /// <summary>
        /// Holds the session ID.
        /// </summary>
        private string? _sessionID;

        /// <summary>
        /// Holds the voice server token.
        /// </summary>
        private string? _token;

        /// <summary>
        /// Holds a value indicating whether the client's current session is resumable.
        /// </summary>
        private bool _isSessionResumable;

        /// <summary>
        /// Holds a value indicating that the client should reconnect and resume at its earliest convenience.
        /// </summary>
        private bool _shouldReconnect;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscordVoiceClient"/> class.
        /// </summary>
        /// <param name="gatewayClient">The gateway client.</param>
        /// <param name="connectionWaiterService">The connection waiter service.</param>
        /// <param name="transportService">The voice payload transport service.</param>
        public DiscordVoiceClient
        (
            DiscordGatewayClient gatewayClient,
            IConnectionEstablishmentWaiterService connectionWaiterService,
            IVoicePayloadTransportService transportService
        )
        {
            _gatewayClient = gatewayClient;
            _connectionWaiterService = connectionWaiterService;
            _transportService = transportService;

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
        /// <param name="guildID">The ID of the guild containing the voice channel to connect to.</param>
        /// <param name="channelID">The ID of the voice channel to connect to.</param>
        /// <param name="selfDeafen">Indicates whether the bot user should deafen itself.</param>
        /// <param name="selfMute">Indicates whether the bot user should mute itself.</param>
        /// <param name="ct">A token by which the caller can request this method to stop.</param>
        /// <returns>A gateway connection result which may or may not have succeeded.</returns>
        public async Task<Result> RunAsync(Snowflake guildID, Snowflake channelID, bool selfDeafen, bool selfMute, CancellationToken ct)
        {
            try
            {
                if (_connectionStatus is not GatewayConnectionStatus.Offline)
                {
                    return new InvalidOperationError("Already connected.");
                }

                while (!ct.IsCancellationRequested)
                {

                }

                Result runIterationResult = await RunConnectionIterationAsync(guildID, selfMute, selfDeafen, channelID, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // This is fine
            }
            catch (OperationCanceledException)
            {
                // This is fine
            }
            catch (Exception ex)
            {
                return ex;
            }
            finally
            {
                SendDisconnectVoiceStateUpdate(guildID);
            }

            return Result.FromSuccess();
        }

        /// <summary>
        /// Runs a single iteration of the connection loop.
        /// </summary>
        /// <param name="ct">A token for requests to stop the outer run loop.</param>
        /// <returns>A connection result, based on the results of the iteration.</returns>
        private async Task<Result> RunConnectionIterationAsync(Snowflake guildID, bool selfMute, bool selfDeafen, Snowflake channelID, CancellationToken ct)
        {
            switch (_connectionStatus)
            {
                case GatewayConnectionStatus.Offline:
                case GatewayConnectionStatus.Disconnected:
                    {
                        _gatewayClient.SubmitCommand
                        (
                            new UpdateVoiceState
                            (
                                guildID,
                                selfMute,
                                selfDeafen,
                                channelID
                            )
                        );

                        Result<VoiceConnectionEstablishmentDetails> getConnectionDetails = await _connectionWaiterService.WaitForRequestConfirmation
                        (
                            guildID,
                            ct
                        ).ConfigureAwait(false);

                        if (!getConnectionDetails.IsDefined())
                        {
                            return Result.FromError(getConnectionDetails);
                        }

                        // Using the full namespace here to help avoid potential confusion between the normal and voice gateway event sets.
                        API.Abstractions.Gateway.Events.IVoiceStateUpdate voiceState = getConnectionDetails.Entity.VoiceState;
                        API.Abstractions.Gateway.Events.IVoiceServerUpdate voiceServer = getConnectionDetails.Entity.VoiceServer;

                        if (voiceServer.Endpoint is null)
                        {
                            return new VoiceServerUnavailableError();
                        }

                        string endpoint = $"wss://{voiceServer.Endpoint}?v=4";
                        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var gatewayUri))
                        {
                            return new GatewayError
                            (
                                "Failed to parse the received voice gateway endpoint.",
                                true
                            );
                        }

                        Result connectResult = await _transportService.ConnectAsync(gatewayUri, ct).ConfigureAwait(false);
                        if (!connectResult.IsSuccess)
                        {
                            return connectResult;
                        }

                        Console.WriteLine("Connected, waiting on hello...");

                        Result<IVoicePayload> helloPayload = await _transportService.ReceivePayloadAsync(ct).ConfigureAwait(false);
                        if (!helloPayload.IsDefined())
                        {
                            return Result.FromError
                            (
                                new VoiceGatewayError("The first payload from the voice gateway was not a hello. Rude!", true),
                                helloPayload
                            );
                        }

                        Console.WriteLine("Hello received succesfully, sending identify...");

                        if (helloPayload.Entity is not IVoicePayload<IVoiceHello> hello)
                        {
                            // Not receiving a hello is a non-recoverable error
                            return new GatewayError
                            (
                                "The first payload from the gateway was not a hello. Rude!",
                                true
                            );
                        }

                        /* TODO: Setup heartbeating */

                        // Set up the send task
                        var heartbeatInterval = hello.Data.HeartbeatInterval;

                        _sendTask = GatewaySenderAsync(heartbeatInterval, _disconnectRequestedSource.Token);

                        // Attempt to connect or resume
                        Result handshakeResult = await AttemptHandshakeAsync
                        (
                            voiceServer.GuildID,
                            voiceState.UserID,
                            voiceState.SessionID,
                            voiceServer.Token,
                            ct
                        ).ConfigureAwait(false);

                        if (!handshakeResult.IsSuccess)
                        {
                            return handshakeResult;
                        }

                        // Now, set up the receive task and start receiving events normally
                        _receiveTask = GatewayReceiverAsync(_disconnectRequestedSource.Token);

                        _shouldReconnect = false;
                        _isSessionResumable = false;
                        _lastReceivedHeartbeatAck = 0;

                        _connectionStatus = GatewayConnectionStatus.Connected;

                        break;
                    }
                case GatewayConnectionStatus.Connected:
                    {
                        // Process received events and dispatch them to the application
                        if (_receivedPayloads.TryDequeue(out var payload))
                        {
                        }

                        // Check the send and receive tasks for errors
                        if (_sendTask.IsCompleted)
                        {
                            var sendResult = await _sendTask;
                            if (!sendResult.IsSuccess)
                            {
                                return sendResult;
                            }
                        }

                        if (_receiveTask.IsCompleted)
                        {
                            var receiveResult = await _receiveTask;
                            if (!receiveResult.IsSuccess)
                            {
                                return receiveResult;
                            }
                        }

                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(10), ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // will cleanup below
                        }

                        break;
                    }
            }

            if (!ct.IsCancellationRequested)
            {
                if (!_shouldReconnect)
                {
                    return Result.FromSuccess();
                }
            }

            // Terminate the send and receive tasks
            _disconnectRequestedSource.Cancel();

            // The results of the send and receive tasks are discarded here, because we know that it's going to be a
            // cancellation
            _ = await _sendTask;
            _ = await _receiveTask;

            var disconnectResult = await _transportService.DisconnectAsync(!ct.IsCancellationRequested, ct);
            if (!disconnectResult.IsSuccess)
            {
                return disconnectResult;
            }

            // Set up the state for the new connection
            _disconnectRequestedSource.Dispose();
            _disconnectRequestedSource = new CancellationTokenSource();
            _connectionStatus = GatewayConnectionStatus.Disconnected;

            return Result.FromSuccess();
        }

        /// <summary>
        /// Attempts to identify or resume the gateway connection.
        /// </summary>
        /// <param name="guildID">The ID of the guild to connect to.</param>
        /// <param name="userID">The ID of the user to connect as.</param>
        /// <param name="sessionID">The session ID.</param>
        /// <param name="token">The server token.</param>
        /// <param name="ct">The cancellation token for this operation.</param>
        /// <returns>A connection result which may or may not have succeeded.</returns>
        private Task<Result> AttemptHandshakeAsync(Snowflake guildID, Snowflake userID, string sessionID, string token, CancellationToken ct = default)
        {
            if (_sessionID is null || !_isSessionResumable)
            {
                // We've never connected before, or the current session isn't resumable
                return CreateNewSessionAsync(guildID, userID, sessionID, token, ct);
            }

            return ResumeExistingSessionAsync(guildID, ct);
        }

        /// <summary>
        /// Creates a new session with the gateway, identifying the client.
        /// </summary>
        /// <param name="ct">The cancellation token for this operation.</param>
        /// <returns>A connection result which may or may not have succeeded.</returns>
        private async Task<Result> CreateNewSessionAsync(Snowflake guildID, Snowflake userID, string sessionID, string token, CancellationToken ct = default)
        {
            Result identifyResult = await SendCommand // TODO: Needs to be thread-safe
            (
                new VoiceIdentify
                (
                    guildID,
                    userID,
                    sessionID,
                    token
                ),
                ct
            ).ConfigureAwait(false);

            if (!identifyResult.IsSuccess)
            {
                return identifyResult;
            }

            Console.WriteLine("Sent identify, waiting on ready response...");

            while (true)
            {
                Result<IVoicePayload> getReadyPayload = await _transportService.ReceivePayloadAsync(ct).ConfigureAwait(false);
                if (!getReadyPayload.IsDefined())
                {
                    return Result.FromError
                    (
                        new VoiceGatewayError("Failed to receive voice ready payload", true),
                        getReadyPayload
                    );
                }

                if (getReadyPayload.Entity is IVoicePayload<IVoiceHeartbeatAcknowledge>)
                {
                    continue;
                }

                if (getReadyPayload.Entity is not IVoicePayload<IVoiceReady> ready)
                {
                    return new GatewayError("The identification response payload was not a Ready payload.", true);
                }

                Console.WriteLine("Ready received successfully");
                _sessionID = sessionID;

                break;
            }

            return Result.FromSuccess();
        }

        /// <summary>
        /// Resumes an existing session with the gateway, replaying missed events.
        /// </summary>
        /// <param name="guildID">The ID of the guild to resume a connection to.</param>
        /// <param name="ct">The cancellation token for this operation.</param>
        /// <returns>A connection result which may or may not have succeeded.</returns>
        private async Task<Result> ResumeExistingSessionAsync(Snowflake guildID, CancellationToken ct = default)
        {
            if (_sessionID is null || _token is null)
            {
                return new InvalidOperationError("There's no previous session to resume.");
            }

            Result identifyResult = await SendCommand // TODO: Needs to be thread-safe
            (
                new VoiceResume
                (
                    guildID,
                    _sessionID,
                    _token
                ),
                ct
            ).ConfigureAwait(false);

            if (!identifyResult.IsSuccess)
            {
                return identifyResult;
            }

            // Push resumed events onto the queue
            var resuming = true;
            while (resuming)
            {
                if (ct.IsCancellationRequested)
                {
                    return new GatewayError("Operation was cancelled.", false);
                }

                var receiveEvent = await _transportService.ReceivePayloadAsync(ct);
                if (!receiveEvent.IsSuccess)
                {
                    return Result.FromError(new GatewayError("Failed to receive a payload.", false), receiveEvent);
                }

                switch (receiveEvent.Entity)
                {
                    case IVoicePayload<IVoiceHeartbeatAcknowledge>:
                        {
                            continue;
                        }
                    case IVoicePayload<IVoiceResumed>:
                        {
                            resuming = false;
                            break;
                        }
                }
            }

            return Result.FromSuccess();
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

        /// <summary>
        /// Sends a voice gateway command to over the transport service.
        /// </summary>
        /// <typeparam name="TCommand">The type of the command to send.</typeparam>
        /// <param name="command">The command object.</param>
        /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        private async Task<Result> SendCommand<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : IVoiceGatewayCommand
        {
            VoicePayload<TCommand> payload = new(command);
            return await _transportService.SendPayloadAsync(payload, ct).ConfigureAwait(false);
        }
    }
}
