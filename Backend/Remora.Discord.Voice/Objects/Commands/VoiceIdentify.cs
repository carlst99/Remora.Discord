//
//  VoiceIdentify.cs
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

using JetBrains.Annotations;
using Remora.Discord.Core;
using Remora.Discord.Voice.Abstractions.Objects.Commands;

namespace Remora.Discord.Voice.Objects.Commands
{
    /// <summary>
    /// <inheritdoc cref="IVoiceIdentify"/>
    /// </summary>
    /// <param name="ServerID"><inheritdoc cref="IVoiceIdentify.ServerID"/></param>
    /// <param name="UserID"><inheritdoc cref="IVoiceIdentify.UserID"/></param>
    /// <param name="SessionID"><inheritdoc cref="IVoiceIdentify.SessionID"/></param>
    /// <param name="Token"><inheritdoc cref="IVoiceIdentify.Token"/></param>
    [PublicAPI]
    public record VoiceIdentify
    (
        Snowflake ServerID,
        Snowflake UserID,
        string SessionID,
        string Token
    ) : IVoiceIdentify;
}
