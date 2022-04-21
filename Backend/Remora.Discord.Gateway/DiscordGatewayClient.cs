//
//  DiscordGatewayClient.cs
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
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remora.Discord.API.Abstractions;
using Remora.Discord.API.Abstractions.Gateway;
using Remora.Discord.API.Abstractions.Gateway.Bidirectional;
using Remora.Discord.API.Abstractions.Gateway.Commands;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.API.Gateway.Bidirectional;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.Gateway.Results;
using Remora.Discord.Gateway.Services;
using Remora.Discord.Gateway.Transport;
using Remora.Discord.Rest;
using Remora.Rest.Core;
using Remora.Results;

namespace Remora.Discord.Gateway;

/// <summary>
/// Provides an implementation of the <see cref="BaseGatewayClient"/> class,
/// intended for connecting to the main Discord gateway.
/// </summary>
[PublicAPI]
public sealed class DiscordGatewayClient : BaseGatewayClient
{
    private readonly ILogger<DiscordGatewayClient> _logger;
    private readonly IDiscordRestGatewayAPI _gatewayAPI;
    private readonly ITokenStore _tokenStore;
    private readonly DiscordGatewayClientOptions _gatewayOptions;
    private readonly Random _random;
    private readonly GatewayHeartbeatData _heartbeatData;

    /// <summary>
    /// Gets the current session ID. This can be used
    /// to resume a session directly upon startup.
    /// </summary>
    public string? SessionID { get; private set; }

    /// <summary>
    /// Gets the last sequence number received by the gateway.
    /// This can be used to resume a session directly upon startup.
    /// </summary>
    public int? LastSequenceNumber => _heartbeatData.LastSequenceNumber == 0
        ? null
        : _heartbeatData.LastSequenceNumber;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordGatewayClient"/> class.
    /// </summary>
    /// <param name="logger">The logging provider.</param>
    /// <param name="transportService">The transport service.</param>
    /// <param name="dispatchService">The dispatch service.</param>
    /// <param name="gatewayAPI">The gateway API.</param>
    /// <param name="gatewayOptions">The gateway options.</param>
    /// <param name="tokenStore">The token store.</param>
    /// <param name="random">An entropy source.</param>
    public DiscordGatewayClient
    (
        ILogger<DiscordGatewayClient> logger,
        IPayloadTransportService transportService,
        IResponderDispatchService dispatchService,
        IDiscordRestGatewayAPI gatewayAPI,
        IOptions<DiscordGatewayClientOptions> gatewayOptions,
        ITokenStore tokenStore,
        Random random
    )
        : base(logger, transportService, dispatchService)
    {
        _logger = logger;
        _gatewayAPI = gatewayAPI;
        _gatewayOptions = gatewayOptions.Value;
        _tokenStore = tokenStore;
        _random = random;

        _heartbeatData = new GatewayHeartbeatData();
    }

    /// <summary>
    /// Runs the gateway client. This task will only complete when the
    /// gateway connection fatally disconnects, or it is cancelled.
    /// This overload will resume an existing session.
    /// </summary>
    /// <param name="sessionID">The ID of the session to resume.</param>
    /// <param name="sequenceNumber">The last received sequence number of the previous session.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
    /// <returns>A result representing the outcome of the operation.</returns>
    public Task<Result> RunAsync
    (
        string sessionID,
        int sequenceNumber,
        CancellationToken ct = default
    )
    {
        this.SessionID = sessionID;
        _heartbeatData.LastSequenceNumber = sequenceNumber;
        this.Status = GatewayConnectionStatus.Disconnected;

        return RunAsync(ct);
    }

    /// <inheritdoc />
    protected override async Task<Result> ConnectAsync(CancellationToken ct)
    {
        // Reset
        _heartbeatData.Interval = TimeSpan.Zero;
        _heartbeatData.LastSentTime = DateTimeOffset.MinValue;
        _heartbeatData.LastReceivedAckTime = DateTimeOffset.MinValue;

        _logger.LogInformation("Retrieving gateway endpoint...");

        // Start connecting
        var getGatewayEndpoint = await _gatewayAPI.GetGatewayBotAsync(ct);
        if (!getGatewayEndpoint.IsSuccess)
        {
            return Result.FromError
            (
                new GatewayError("Failed to get the gateway endpoint.", false, true),
                getGatewayEndpoint
            );
        }

        var endpointInformation = getGatewayEndpoint.Entity;
        if (endpointInformation.Shards.IsDefined(out var shards))
        {
            if (shards > 1 && _gatewayOptions.ShardIdentification?.ShardCount != shards)
            {
                _logger.LogInformation
                (
                    "Discord suggests {Shards} shards for this bot, but your sharding configuration does " +
                    "not match this. Consider switching to a sharded topology of at least that many shards",
                    shards
                );
            }
        }

        if (endpointInformation.SessionStartLimit.IsDefined(out var startLimit))
        {
            if (this.SessionID is null)
            {
                if (startLimit.Remaining <= 0)
                {
                    _logger.LogWarning
                    (
                        "No further sessions may be started right now for this bot. Waiting {Time} for " +
                        "the limits to reset...",
                        startLimit.ResetAfter
                    );

                    await Task.Delay(startLimit.ResetAfter, ct);
                    return new GatewayError("Session start limits reached; retrying...", false, false);
                }

                _logger.LogInformation
                (
                    "Starting a new session ({Remaining} session starts remaining of {Total}; limits " +
                    "reset in {Time})",
                    startLimit.Remaining,
                    startLimit.Total,
                    startLimit.ResetAfter
                );
            }
            else
            {
                _logger.LogInformation
                (
                    "Resuming an existing session ({Remaining} new session starts remaining of {Total}; " +
                    "limits reset in {Time})",
                    startLimit.Remaining,
                    startLimit.Total,
                    startLimit.ResetAfter
                );
            }
        }
        else
        {
            _logger.LogWarning
            (
                "There's no session start limits available for this connection. Rate limits may be " +
                "unexpectedly hit"
            );
        }

        var gatewayEndpoint = $"{getGatewayEndpoint.Entity.Url}?v={(int)DiscordAPIVersion.V10}&encoding=json";
        if (!Uri.TryCreate(gatewayEndpoint, UriKind.Absolute, out var gatewayUri))
        {
            return new GatewayError
            (
                "Failed to parse the received gateway endpoint.",
                false,
                true
            );
        }

        _logger.LogInformation("Connecting to the gateway...");

        var transportConnectResult = await ConnectToGatewayAndBeginSendTaskAsync(gatewayUri, ct);
        if (!transportConnectResult.IsSuccess)
        {
            return transportConnectResult;
        }

        var receiveHello = await this.TransportService.ReceivePayloadAsync(ct);
        if (!receiveHello.IsSuccess)
        {
            return Result.FromError
            (
                new GatewayError("Failed to receive the Hello payload.", false, true),
                receiveHello
            );
        }

        if (receiveHello.Entity is not IPayload<IHello> hello)
        {
            // Let's attempt to reconnect
            return new GatewayError
            (
                "The first payload from the gateway was not a hello. Rude!",
                false,
                false
            );
        }

        await this.DispatchService.EnqueueEventAsync(hello.Data, ct);
        _heartbeatData.Interval = hello.Data.HeartbeatInterval;

        // Attempt to connect or resume
        var connectResult = this.Status is GatewayConnectionStatus.Offline
            ? await CreateNewSessionAsync(ct)
            : await ResumeExistingSessionAsync(ct);

        if (!connectResult.IsSuccess)
        {
            return connectResult;
        }

        this.Status = GatewayConnectionStatus.Connected;
        _logger.LogInformation("Connected");

        return Result.FromSuccess();
    }

    /// <inheritdoc />
    protected override bool ShouldReconnect(Result iterationResult, out bool withNewSession)
    {
        withNewSession = false;

        switch (iterationResult.Error)
        {
            case GatewayDiscordError gde:
            {
                _logger.LogWarning
                (
                    "Remote transient gateway error: {Error}",
                    gde.Message
                );

                switch (gde.CloseStatus)
                {
                    case GatewayCloseStatus.UnknownError:
                    case GatewayCloseStatus.UnknownOpcode:
                    case GatewayCloseStatus.DecodeError:
                    case GatewayCloseStatus.AlreadyAuthenticated:
                    case GatewayCloseStatus.RateLimited:
                    {
                        return true;
                    }
                    case GatewayCloseStatus.NotAuthenticated:
                    case GatewayCloseStatus.InvalidSequence:
                    case GatewayCloseStatus.SessionTimedOut:
                    {
                        withNewSession = true;
                        return true;
                    }
                    case GatewayCloseStatus.AuthenticationFailed:
                    case GatewayCloseStatus.InvalidShard:
                    case GatewayCloseStatus.ShardingRequired:
                    case GatewayCloseStatus.InvalidAPIVersion:
                    case GatewayCloseStatus.InvalidIntents:
                    case GatewayCloseStatus.DisallowedIntent:
                    {
                        return false;
                    }
                }

                break;
            }
            case GatewayWebSocketError gwe:
            {
                _logger.LogWarning
                (
                    "Transient gateway transport layer error: {Error}",
                    gwe.Message
                );

                switch (gwe.CloseStatus)
                {
                    case WebSocketCloseStatus.InternalServerError:
                    case WebSocketCloseStatus.EndpointUnavailable:
                    {
                        withNewSession = true;
                        return true;
                    }
                }

                break;
            }
            case GatewayError gae:
            {
                // We'll try reconnecting on non-critical internal errors
                if (!gae.IsCritical)
                {
                    _logger.LogWarning
                    (
                        "Local transient gateway error: {Error}",
                        gae.Message
                    );

                    withNewSession = !gae.IsSessionResumable;
                    return true;
                }

                _logger.LogError
                (
                    "Local unrecoverable gateway error: {Error}",
                    gae.Message
                );

                return false;
            }
            case ExceptionError exe:
            {
                withNewSession = true;

                switch (exe.Exception)
                {
                    case HttpRequestException or WebSocketException:
                    {
                        _logger.LogWarning
                        (
                            exe.Exception,
                            "Transient error in gateway client: {Exception}",
                            exe.Message
                        );

                        return true;
                    }
                    default:
                    {
                        return false;
                    }
                }
            }
        }

        _logger.LogWarning
        (
            "Transient error in gateway client: {Error}",
            iterationResult
        );

        // We don't know what happened... try reconnecting?
        return true;
    }

    /// <inheritdoc />
    protected override bool ProcessPayload(IPayload payload)
    {
        // Update the sequence number
        if (payload is IEventPayload eventPayload)
        {
            _heartbeatData.LastSequenceNumber = eventPayload.SequenceNumber;
        }

        // Update the ack timestamp
        if (payload is IPayload<IHeartbeatAcknowledge>)
        {
            UpdateLastReceivedHeartbeatToNow();
        }

        switch (payload)
        {
            case IPayload<IReconnect>:
            {
                _logger.LogDebug("Reconnect requested by gateway");
                this.Status = GatewayConnectionStatus.Disconnected;

                break;
            }
            case IPayload<IInvalidSession> invalidSession:
            {
                _logger.LogDebug("Session invalidated by gateway");
                this.Status = invalidSession.Data.IsResumable
                    ? GatewayConnectionStatus.Disconnected
                    : GatewayConnectionStatus.Offline;

                if (!invalidSession.Data.IsResumable)
                {
                    this.SessionID = null;
                }

                break;
            }
            case IPayload<IHeartbeat>:
            {
                EnqueuePriorityCommand
                (
                    new Heartbeat
                    (
                        _heartbeatData.LastSequenceNumber == 0 ? null : _heartbeatData.LastSequenceNumber
                    )
                );

                break;
            }
        }

        return true;
    }

    /// <inheritdoc />
    protected override Result<TimeSpan> SendHeartbeat()
    {
        if (_heartbeatData.Interval == TimeSpan.Zero)
        {
            // We have not received OP code hello yet, so don't heartbeat
            return TimeSpan.FromMilliseconds(50);
        }

        // Heartbeat, if required
        var interval = _heartbeatData.Interval;
        var safetyMargin = _gatewayOptions.GetTrueHeartbeatSafetyMargin(interval);
        var lastSentTime = _heartbeatData.LastSentTime;
        var now = DateTimeOffset.UtcNow;

        if (lastSentTime is not null && now - lastSentTime < interval - safetyMargin)
        {
            return lastSentTime.Value + _heartbeatData.Interval - safetyMargin - now;
        }

        if (_heartbeatData.LastReceivedAckTime < _heartbeatData.LastSentTime)
        {
            return new GatewayError
            (
                "The server did not respond in time with a heartbeat acknowledgement.",
                true,
                false
            );
        }

        EnqueuePriorityCommand
        (
            new Heartbeat
            (
                _heartbeatData.LastSequenceNumber == 0 ? null : _heartbeatData.LastSequenceNumber
            )
        );

        _heartbeatData.LastSentTime = now;
        return _heartbeatData.Interval - safetyMargin;
    }

    /// <summary>
    /// Creates a new session with the gateway, identifying the client.
    /// </summary>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A connection result which may or may not have succeeded.</returns>
    private async Task<Result> CreateNewSessionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Creating a new session...");

        var shardInformation = _gatewayOptions.ShardIdentification is null
            ? default
            : new Optional<IShardIdentification>(_gatewayOptions.ShardIdentification);

        var initialPresence = _gatewayOptions.Presence is null
            ? default
            : new Optional<IUpdatePresence>(_gatewayOptions.Presence);

        EnqueuePriorityCommand
        (
            new Identify
            (
                _tokenStore.Token,
                _gatewayOptions.ConnectionProperties,
                Intents: _gatewayOptions.Intents,
                Compress: false,
                Shard: shardInformation,
                Presence: initialPresence,
                LargeThreshold: _gatewayOptions.LargeThreshold
            )
        );

        while (true)
        {
            var receiveReady = await this.TransportService.ReceivePayloadAsync(ct);
            if (!receiveReady.IsSuccess)
            {
                return Result.FromError(receiveReady);
            }

            if (receiveReady.Entity is IPayload<IHeartbeatAcknowledge>)
            {
                UpdateLastReceivedHeartbeatToNow();
                continue;
            }

            if (receiveReady.Entity is IPayload<IInvalidSession> invalidSession)
            {
                return new GatewayError
                (
                    "Session invalidated",
                    invalidSession.Data.IsResumable,
                    false
                );
            }

            if (receiveReady.Entity is not IPayload<IReady> ready)
            {
                _logger.LogTrace("Payload Body: {Body}", JsonSerializer.Serialize(receiveReady.Entity));
                return new GatewayError
                (
                    $"The payload after identification was not a Ready payload.{Environment.NewLine}" +
                    $"\tExpected: {typeof(IPayload<IReady>).FullName}{Environment.NewLine}" +
                    $"\tActual: {receiveReady.Entity.GetType().FullName}",
                    false,
                    false
                );
            }

            if (receiveReady.Entity is IPayload<IGatewayEvent> gatewayEvent)
            {
                await this.DispatchService.EnqueueEventAsync(gatewayEvent.Data, ct);
            }

            this.SessionID = ready.Data.SessionID;
            break;
        }

        return Result.FromSuccess();
    }

    /// <summary>
    /// Resumes an existing session with the gateway, replaying missed events.
    /// </summary>
    /// <param name="ct">The cancellation token for this operation.</param>
    /// <returns>A connection result which may or may not have succeeded.</returns>
    private async Task<Result> ResumeExistingSessionAsync(CancellationToken ct = default)
    {
        if (this.SessionID is null)
        {
            return new InvalidOperationError("There's no previous session to resume.");
        }

        _logger.LogInformation("Resuming existing session...");

        EnqueuePriorityCommand
        (
            new Resume
            (
                _tokenStore.Token,
                this.SessionID,
                _heartbeatData.LastSequenceNumber
            )
        );

        // Push resumed events onto the queue
        var resuming = true;
        while (resuming)
        {
            if (ct.IsCancellationRequested)
            {
                return new GatewayError("Operation was cancelled.", false, false);
            }

            var receiveEvent = await this.TransportService.ReceivePayloadAsync(ct);
            if (!receiveEvent.IsSuccess)
            {
                return Result.FromError
                (
                    new GatewayError("Failed to receive a payload.", false, false),
                    receiveEvent
                );
            }

            switch (receiveEvent.Entity)
            {
                case IPayload<IHeartbeatAcknowledge>:
                {
                    UpdateLastReceivedHeartbeatToNow();
                    continue;
                }
                case IPayload<IInvalidSession>:
                {
                    _logger.LogInformation("Resume rejected by the gateway");

                    await Task.Delay(_random.Next(1000, 5000), ct);
                    return await CreateNewSessionAsync(ct);
                }
                case IPayload<IResumed>:
                {
                    resuming = false;
                    break;
                }
            }

            if (receiveEvent.Entity is IPayload<IGatewayEvent> gatewayEvent)
            {
                await this.DispatchService.EnqueueEventAsync(gatewayEvent.Data, ct);
            }
        }

        return Result.FromSuccess();
    }

    private void UpdateLastReceivedHeartbeatToNow()
    {
        _heartbeatData.LastReceivedAckTime = DateTimeOffset.UtcNow;

        if (_heartbeatData.LastSentTime is not null)
        {
            this.Latency = (TimeSpan)(_heartbeatData.LastReceivedAckTime - _heartbeatData.LastSentTime);
        }
    }
}
