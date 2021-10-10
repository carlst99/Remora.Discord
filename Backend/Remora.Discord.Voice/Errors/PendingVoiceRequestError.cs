//
//  PendingVoiceRequestError.cs
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

using Remora.Discord.Core;
using Remora.Results;

namespace Remora.Discord.Voice.Errors
{
    /// <summary>
    /// Represents a failure to request a new voice connection, as a request already pending for the given guild.
    /// </summary>
    public record PendingVoiceRequestError : ResultError
    {
        /// <summary>
        /// Gets the ID of the guild for which a pending voice connection request exists.
        /// </summary>
        public Snowflake GuildID { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PendingVoiceRequestError"/> class.
        /// </summary>
        /// <param name="guildID">The ID of the guild for which a pending voice connection request exists.</param>
        public PendingVoiceRequestError(Snowflake guildID)
            : base("A connection request is already pending for this guild.")
        {
            GuildID = guildID;
        }
    }
}
