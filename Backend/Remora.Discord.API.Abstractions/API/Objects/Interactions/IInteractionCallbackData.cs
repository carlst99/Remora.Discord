//
//  IInteractionCallbackData.cs
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
using JetBrains.Annotations;
using Remora.Discord.Core;

namespace Remora.Discord.API.Abstractions.Objects
{
    /// <summary>
    /// Represents return payload data for an interaction response.
    /// </summary>
    [PublicAPI]
    public interface IInteractionCallbackData
    {
        /// <summary>
        /// Gets a value indicating whether the message is a TTS message.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<bool> IsTTS { get; }

        /// <summary>
        /// Gets the content of the message.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<string> Content { get; }

        /// <summary>
        /// Gets the embeds of the message.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<IReadOnlyList<IEmbed>> Embeds { get; }

        /// <summary>
        /// Gets the allowed mentions in the message.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<IAllowedMentions> AllowedMentions { get; }

        /// <summary>
        /// Gets the callback flags.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<InteractionCallbackDataFlags> Flags { get; }

        /// <summary>
        /// Gets the components attached to the message.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<IReadOnlyList<IMessageComponent>> Components { get; }

        /// <summary>
        /// Gets the attachments attached to the message.
        /// </summary>
        /// <remarks>Only relevant for message interactions.</remarks>
        Optional<IReadOnlyList<IPartialAttachment>> Attachments { get; }

        /// <summary>
        /// Gets the autocomplete choices.
        /// </summary>
        /// <remarks>Only relevant for autocomplete interactions.</remarks>
        Optional<IReadOnlyList<IApplicationCommandOptionChoice>> Choices { get; }
    }
}
