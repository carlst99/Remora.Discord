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
using Remora.Discord.Voice.Objects;
using Remora.Discord.Voice.Services.Abstractions;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscordVoiceClient"/> class.
        /// </summary>
        /// <param name="gatewayClient">The gateway client.</param>
        /// <param name="connectionWaiterService">The connection waiter service.</param>
        public DiscordVoiceClient
        (
            DiscordGatewayClient gatewayClient,
            IConnectionEstablishmentWaiterService connectionWaiterService
        )
        {
            _gatewayClient = gatewayClient;
            _connectionWaiterService = connectionWaiterService;
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

                Result<VoiceConnectionEstablishmentDetails> getConnectionDetails = await _connectionWaiterService.WaitForRequestConfirmation(guildID, ct);
                if (!getConnectionDetails.IsDefined())
                {
                    return Result.FromError(getConnectionDetails);
                }
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

            return Result.FromSuccess();
        }
    }
}
