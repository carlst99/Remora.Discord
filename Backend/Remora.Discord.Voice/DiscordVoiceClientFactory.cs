//
//  DiscordVoiceClientFactory.cs
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Remora.Discord.API.Gateway.Commands;
using Remora.Discord.Core;
using Remora.Discord.Gateway;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Results;

namespace Remora.Discord.Voice
{
    /// <summary>
    /// Represents a factory for obtaining instances of a <see cref="DiscordVoiceClient"/>.
    /// </summary>
    [PublicAPI]
    public sealed class DiscordVoiceClientFactory : IAsyncDisposable
    {
        private readonly IServiceProvider _services;
        private readonly Dictionary<Snowflake, DiscordVoiceClient> _guildClients;
        private readonly ConcurrentDictionary<Snowflake, (CancellationTokenSource, Task<Result>)> _runningClients;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscordVoiceClientFactory"/> class.
        /// </summary>
        /// <param name="services">The service provider.</param>
        public DiscordVoiceClientFactory(IServiceProvider services)
        {
            _services = services;
            _guildClients = new Dictionary<Snowflake, DiscordVoiceClient>();
            _runningClients = new ConcurrentDictionary<Snowflake, (CancellationTokenSource, Task<Result>)>();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            foreach (Snowflake element in _runningClients.Keys)
            {
                await StopAsync(element).ConfigureAwait(false);
            }

            foreach ((CancellationTokenSource, Task<Result>) element in _runningClients.Values)
            {
                element.Item1.Dispose();
                element.Item2.Dispose();
            }
        }

        /// <summary>
        /// Gets a voice client for the given guild.
        /// </summary>
        /// <param name="guildID">The ID of the guild to retrieve a voice client for.</param>
        /// <returns>A voice client instance.</returns>
        public DiscordVoiceClient Get(Snowflake guildID)
        {
            if (_guildClients.ContainsKey(guildID))
            {
                return _guildClients[guildID];
            }

            var voiceClient = new DiscordVoiceClient
            (
                _services.GetRequiredService<DiscordGatewayClient>(),
                _services.GetRequiredService<IOptions<DiscordVoiceClientOptions>>(),
                _services.GetRequiredService<IConnectionEstablishmentWaiterService>(),
                _services.GetRequiredService<IVoicePayloadTransportService>(),
                _services.GetRequiredService<Random>()
            );

            _guildClients.Add(guildID, voiceClient);

            return voiceClient;
        }

        /// <summary>
        /// Runs a voice client.
        /// </summary>
        /// <param name="guildID">The ID of guild containing the voice channel we wish to connect to.</param>
        /// <param name="channelID">The ID of the channel to connect to.</param>
        /// <param name="isSelfMuted">Sets a value indicating whether or not the bot should mute itself.</param>
        /// <param name="isSelfDeafened">Sets a value indicating whether or not the bot should deafen itself.</param>
        /// <returns>A <see cref="Result"/> representing the outcome of the operation.</returns>
        public async ValueTask<Result> RunAsync(Snowflake guildID, Snowflake channelID, bool isSelfMuted, bool isSelfDeafened)
        {
            UpdateVoiceState connectionParameters = new
            (
                guildID,
                isSelfMuted,
                isSelfDeafened,
                channelID
            );

            DiscordVoiceClient client = Get(connectionParameters.GuildID);
            if (client.ConnectionStatus is not GatewayConnectionStatus.Offline)
            {
                return new InvalidOperationError("This voice client is already running.");
            }

            CancellationTokenSource cts = new();
            Task<Result> runTask = client.RunAsync(connectionParameters, cts.Token);

            if (!_runningClients.TryAdd(connectionParameters.GuildID, (cts, runTask)))
            {
                cts.Cancel();
                await runTask.ConfigureAwait(false);

                return new InvalidOperationError("This voice client is already running.");
            }

            return Result.FromSuccess();
        }

        /// <summary>
        /// Stops a voice client that was run by this factory.
        /// </summary>
        /// <param name="guildID">The ID of the guild to stop the client for.</param>
        /// <returns>The result of the voice client.</returns>
        public async Task<Result> StopAsync(Snowflake guildID)
        {
            if (!_runningClients.TryRemove(guildID, out (CancellationTokenSource Cts, Task<Result> Task) client))
            {
                return new InvalidOperationError("This voice client was not running.");
            }

            if (!client.Task.IsCompleted)
            {
                client.Cts.Cancel();
            }

            Result d = await client.Task.ConfigureAwait(false);
            return d;
        }
    }
}
