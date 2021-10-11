//
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
using Remora.Discord.API.Extensions;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Voice.Abstractions.Objects.Commands.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Objects.Commands.ConnectingResuming;
using Remora.Discord.Voice.Responders;
using Remora.Discord.Voice.Services;
using System.Text.Json;

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
        /// <remarks>
        /// This method expects that the gateway services have been registered - see
        /// <see cref="ServiceCollectionExtensions.AddDiscordGateway(IServiceCollection, System.Func{System.IServiceProvider, string})"/>.
        /// </remarks>
        /// <param name="serviceCollection">The service collection.</param>
        /// <returns>The service collection, with the services added.</returns>
        public static IServiceCollection AddDiscordVoice(this IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddTransient<IVoicePayloadTransportService, WebSocketVoicePayloadTransportService>();

            serviceCollection.TryAddSingleton<IConnectionEstablishmentWaiterService, ConnectionEstablishmentWaiterService>();
            serviceCollection.TryAddSingleton<DiscordVoiceClientFactory>();

            serviceCollection.AddResponder<VoiceStateUpdateResponder>();
            serviceCollection.AddResponder<VoiceServerUpdateResponder>();

            serviceCollection
                .Configure<JsonSerializerOptions>
                (
                    options =>
                    {
                        //options.Converters.Add(new PayloadConverter(allowUnknownEvents));

                        options
                            .AddVoiceGatewayBidirectionalConverters()
                            .AddVoiceGatewayCommandConverters()
                            .AddVoiceGatewayEventConverters();

                        //options.AddDataObjectConverter<IUnknownEvent, UnknownEvent>();
                    }
                );

            return serviceCollection;
        }

        /// <summary>
        /// Adds the JSON converters that handle bidirectional gateway payloads.
        /// </summary>
        /// <param name="options">The serializer options.</param>
        /// <returns>The options, with the converters added.</returns>
        private static JsonSerializerOptions AddVoiceGatewayBidirectionalConverters(this JsonSerializerOptions options)
        {
            //options
            //    .AddConverter<HeartbeatConverter>()
            //    .AddDataObjectConverter<IHeartbeatAcknowledge, HeartbeatAcknowledge>();

            return options;
        }

        /// <summary>
        /// Adds the JSON converters that handle gateway command payloads.
        /// </summary>
        /// <param name="options">The serializer options.</param>
        /// <returns>The options, with the converters added.</returns>
        private static JsonSerializerOptions AddVoiceGatewayCommandConverters(this JsonSerializerOptions options)
        {
            options.AddDataObjectConverter<IVoiceIdentify, VoiceIdentify>();

            //options.AddDataObjectConverter<IUpdatePresence, UpdatePresence>()
            //    .WithPropertyName(u => u.IsAFK, "afk")
            //    .WithPropertyConverter
            //    (
            //        u => u.Status,
            //        new StringEnumConverter<ClientStatus>(new SnakeCaseNamingPolicy())
            //    )
            //    .WithPropertyConverter(u => u.Since, new UnixMillisecondsDateTimeOffsetConverter());

            return options;
        }

        /// <summary>
        /// Adds the JSON converters that handle gateway event payloads.
        /// </summary>
        /// <param name="options">The serializer options.</param>
        /// <returns>The options, with the converters added.</returns>
        private static JsonSerializerOptions AddVoiceGatewayEventConverters(this JsonSerializerOptions options)
        {
            // Connecting and resuming
            //options.AddDataObjectConverter<IHello, Hello>()
            //    .WithPropertyConverter(h => h.HeartbeatInterval, new UnitTimeSpanConverter(TimeUnit.Milliseconds));

            options.AddDataObjectConverter<IVoiceReady, VoiceReady>()
                .WithPropertyName(r => r.Version, "v");

            //options.AddDataObjectConverter<IReconnect, Reconnect>();
            //options.AddDataObjectConverter<IResumed, Resumed>();

            //// Channels
            //options.AddDataObjectConverter<IChannelCreate, ChannelCreate>()
            //    .WithPropertyName(c => c.IsNsfw, "nsfw")
            //    .WithPropertyConverter(c => c.RateLimitPerUser, new UnitTimeSpanConverter(TimeUnit.Seconds));

            //// Other
            //options.AddDataObjectConverter<IUnknownEvent, UnknownEvent>();

            return options;
        }
    }
}
