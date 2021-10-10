﻿//
//  ConnectionEstablishmentWaiterService.cs
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
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.API.Abstractions.Rest;
using Remora.Discord.Core;
using Remora.Discord.Voice.Errors;
using Remora.Discord.Voice.Objects;
using Remora.Discord.Voice.Services.Abstractions;
using Remora.Results;

namespace Remora.Discord.Voice.Services
{
    /// <inheritdoc cref="IConnectionEstablishmentWaiterService"/>
    public sealed class ConnectionEstablishmentWaiterService : IConnectionEstablishmentWaiterService
    {
        /// <summary>
        /// Defines the amount of time in milliseconds before a connection waiter times out.
        /// </summary>
        public const int ConnectionWaitTimeoutMS = 5000;

        private readonly IDiscordRestUserAPI _userAPI;
        private readonly HashSet<Snowflake> _pendingRequests;
        private readonly ConcurrentDictionary<Snowflake, IVoiceStateUpdate> _stateUpdates;
        private readonly ConcurrentDictionary<Snowflake, IVoiceServerUpdate> _serverUpdates;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionEstablishmentWaiterService"/> class.
        /// </summary>
        /// <param name="userAPI">The Discord User API.</param>
        public ConnectionEstablishmentWaiterService(IDiscordRestUserAPI userAPI)
        {
            _userAPI = userAPI;
            _pendingRequests = new HashSet<Snowflake>();
            _stateUpdates = new ConcurrentDictionary<Snowflake, IVoiceStateUpdate>();
            _serverUpdates = new ConcurrentDictionary<Snowflake, IVoiceServerUpdate>();
        }

        /// <inheritdoc />
        public void SubmitVoiceServerUpdate(IVoiceServerUpdate voiceServerUpdate)
        {
            if (!_pendingRequests.Contains(voiceServerUpdate.GuildID))
            {
                return;
            }

            _serverUpdates.TryAdd(voiceServerUpdate.GuildID, voiceServerUpdate);
        }

        /// <inheritdoc />
        public async Task SubmitVoiceStateUpdate(IVoiceStateUpdate voiceStateUpdate, CancellationToken ct = default)
        {
            if (!voiceStateUpdate.GuildID.IsDefined() || !_pendingRequests.Contains(voiceStateUpdate.GuildID.Value))
            {
                return;
            }

            Result<IUser> getCurrentUser = await _userAPI.GetCurrentUserAsync(ct);
            if (getCurrentUser.IsDefined())
            {
                return;
            }

            if (getCurrentUser.Entity.ID != voiceStateUpdate.UserID)
            {
                return;
            }

            _stateUpdates.TryAdd(voiceStateUpdate.GuildID.Value, voiceStateUpdate);
        }

        /// <inheritdoc />
        public async Task<Result<VoiceConnectionEstablishmentDetails>> WaitForRequestConfirmation(Snowflake guildID, CancellationToken ct = default)
        {
            if (_pendingRequests.Contains(guildID))
            {
                return new PendingVoiceRequestError(guildID);
            }

            _pendingRequests.Add(guildID);
            DateTimeOffset requestWaitBeganAt = DateTimeOffset.UtcNow;

            while (!ct.IsCancellationRequested)
            {
                if (requestWaitBeganAt.AddMilliseconds(ConnectionWaitTimeoutMS) < DateTimeOffset.UtcNow)
                {
                    return new VoiceRequestTimeoutError();
                }

                await Task.Delay(100, ct);

                if (!_stateUpdates.ContainsKey(guildID) || !_serverUpdates.ContainsKey(guildID))
                {
                    continue;
                }

                if (!_stateUpdates.TryRemove(guildID, out IVoiceStateUpdate stateUpdate))
                {
                    return new KeyNotFoundException("Internal state was unexpectedly modified.");
                }

                if (!_serverUpdates.TryRemove(guildID, out IVoiceServerUpdate serverUpdate))
                {
                    return new KeyNotFoundException("Internal state was unexpectedly modified.");
                }

                return new VoiceConnectionEstablishmentDetails(stateUpdate, serverUpdate);
            }

            return new TaskCanceledException();
        }
    }
}
