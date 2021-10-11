﻿//
//  IVoiceSpeaking.cs
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
using Remora.Discord.Voice.Abstractions.Objects.Events;

namespace Remora.Discord.Voice.Abstractions.Objects.Bidrectional
{
    /// <summary>
    /// Represents data that a client is speaking.
    /// </summary>
    [PublicAPI]
    public interface IVoiceSpeaking : IVoiceGatewayCommand, IVoiceGatewayEvent
    {
        /// <summary>
        /// Gets the ID of the user who speaking.
        /// </summary>
        Snowflake UserID { get; }

        /// <summary>
        /// Gets the speaker flags.
        /// </summary>
        SpeakingFlags Speaking { get; }

        /// <summary>
        /// Gets the synchronization source ID that this speech event is associated with.
        /// </summary>
        uint SSRC { get; }
    }
}
