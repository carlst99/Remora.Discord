//
//  IGuildEmojisUpdate.cs
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

using System.Collections.Generic;
using Remora.Discord.API.Abstractions.Emojis;
using Remora.Discord.Core;

namespace Remora.Discord.API.Abstractions.Events
{
    /// <summary>
    /// Represents an update to a guild's emojis.
    /// </summary>
    public interface IGuildEmojisUpdate : IGatewayEvent
    {
        /// <summary>
        /// Gets the ID of the guild.
        /// </summary>
        Snowflake GuildID { get; }

        /// <summary>
        /// Gets the guild's emojis.
        /// </summary>
        IReadOnlyList<IEmoji> Emojis { get; }
    }
}
