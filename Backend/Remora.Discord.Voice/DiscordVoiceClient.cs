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
    public class DiscordVoiceClient
    {
        private readonly DiscordGatewayClient _gatewayClient;
        private readonly IConnectionEstablishmentWaiterService _connectionWaiterService;
        private readonly IVoicePayloadTransportService _transportService;

        private GatewayConnectionStatus _connectionStatus;

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
                if (_connectionStatus != GatewayConnectionStatus.Offline)
                {
                    return new InvalidOperationError("Already connected.");
                }

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

                /* TODO: Setup heartbeating */

                Console.WriteLine("Hello received succesfully, sending identify...");

                Result identifyResult = await SendCommand
                (
                    new VoiceIdentify
                    (
                        voiceServer.GuildID,
                        voiceState.UserID,
                        voiceState.SessionID,
                        voiceServer.Token
                    ),
                    ct
                ).ConfigureAwait(false);

                if (!identifyResult.IsSuccess)
                {
                    return identifyResult;
                }

                Console.WriteLine("Sent identify, waiting on ready response...");

                Result<IVoicePayload> readyPayload = await _transportService.ReceivePayloadAsync(ct).ConfigureAwait(false);
                if (!readyPayload.IsDefined())
                {
                    return Result.FromError
                    (
                        new VoiceGatewayError("Failed to receive voice ready payload", true),
                        readyPayload
                    );
                }

                if (readyPayload.Entity is not IVoicePayload<IVoiceReady> hello)
                {
                    return new GatewayError("The first payload from the gateway was not a ready message.", true);
                }

                Console.WriteLine("Ready received successfully");
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
        /// Sends an <see cref="UpdateVoiceState"/> command to the gateway, requesting that the bot be disconnected from a guild's voice channel.
        /// </summary>
        /// <param name="guildID">The guild containing the voice channel to disconnect from.</param>
        protected void SendDisconnectVoiceStateUpdate(Snowflake guildID)
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
        protected async Task<Result> SendCommand<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : IVoiceGatewayCommand
        {
            VoicePayload<TCommand> payload = new(command);
            return await _transportService.SendPayloadAsync(payload, ct).ConfigureAwait(false);
        }
    }
}
