﻿//
//  IServiceCollectionExtensions.cs
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Voice.Responders;
using Remora.Discord.Voice.Services;
using Remora.Discord.Voice.Services.Abstractions;

namespace Remora.Discord.Voice.Extensions
{
    /// <summary>
    /// Defines extension methods for the <see cref="IServiceCollection"/> class.
    /// </summary>
    [PublicAPI]
    public static class IServiceCollectionExtensions
    {
        /// <summary>
        /// Adds services required by the Discord Gateway system.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <returns>The service collection, with the services added.</returns>
        public static IServiceCollection AddDiscordVoice(this IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<IConnectionEstablishmentWaiterService, ConnectionEstablishmentWaiterService>();
            serviceCollection.TryAddSingleton<DiscordVoiceClientFactory>();

            serviceCollection.AddResponder<VoiceStateUpdateResponder>();
            serviceCollection.AddResponder<VoiceServerUpdateResponder>();

            return serviceCollection;
        }
    }
}
