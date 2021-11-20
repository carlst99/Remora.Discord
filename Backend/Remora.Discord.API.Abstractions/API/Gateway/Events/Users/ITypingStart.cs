//
//  ITypingStart.cs
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
using JetBrains.Annotations;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Rest.Core;

namespace Remora.Discord.API.Abstractions.Gateway.Events
{
    /// <summary>
    /// Sent when a user starts typing in a channel.
    /// </summary>
    [PublicAPI]
    public interface ITypingStart : IGatewayEvent
    {
        /// <summary>
        /// Gets the ID of the channel.
        /// </summary>
        Snowflake ChannelID { get; }

        /// <summary>
        /// Gets the ID of the guild.
        /// </summary>
        Optional<Snowflake> GuildID { get; }

        /// <summary>
        /// Gets the ID of the user.
        /// </summary>
        Snowflake UserID { get; }

        /// <summary>
        /// Gets the unix time (in seconds) when the user started typing.
        /// </summary>
        DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Gets the member who started typing (if it happened in a guild).
        /// </summary>
        Optional<IGuildMember> Member { get; }
    }
}
