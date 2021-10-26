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

using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IO;
using Remora.Discord.API.Extensions;
using Remora.Discord.API.Json;
using Remora.Discord.Gateway.Extensions;
using Remora.Discord.Voice.Abstractions.Objects.Commands.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.Commands.Heartbeats;
using Remora.Discord.Voice.Abstractions.Objects.Commands.Protocols;
using Remora.Discord.Voice.Abstractions.Objects.Events.Clients;
using Remora.Discord.Voice.Abstractions.Objects.Events.Codecs;
using Remora.Discord.Voice.Abstractions.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Abstractions.Objects.Events.Heartbeats;
using Remora.Discord.Voice.Abstractions.Objects.Events.Sessions;
using Remora.Discord.Voice.Abstractions.Services;
using Remora.Discord.Voice.Json;
using Remora.Discord.Voice.Objects.Commands.ConnectingResuming;
using Remora.Discord.Voice.Objects.Commands.Heartbeats;
using Remora.Discord.Voice.Objects.Commands.Protocols;
using Remora.Discord.Voice.Objects.Events.Clients;
using Remora.Discord.Voice.Objects.Events.Codecs;
using Remora.Discord.Voice.Objects.Events.ConnectingResuming;
using Remora.Discord.Voice.Objects.Events.Heartbeats;
using Remora.Discord.Voice.Objects.Events.Sessions;
using Remora.Discord.Voice.Responders;
using Remora.Discord.Voice.Services;

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
        /// <see cref="Gateway.Extensions.ServiceCollectionExtensions.AddDiscordGateway(IServiceCollection, System.Func{System.IServiceProvider, string})"/>.
        /// </remarks>
        /// <param name="serviceCollection">The service collection.</param>
        /// <returns>The service collection, with the services added.</returns>
        public static IServiceCollection AddDiscordVoice(this IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<RecyclableMemoryStreamManager>();
            serviceCollection.TryAddSingleton<IConnectionEstablishmentWaiterService, ConnectionEstablishmentWaiterService>();
            serviceCollection.TryAddTransient<IVoicePayloadTransportService, WebSocketVoicePayloadTransportService>();

            serviceCollection.TryAddTransient<DiscordVoiceClient>();
            serviceCollection.TryAddSingleton<DiscordVoiceClientFactory>();

            serviceCollection.TryAddTransient<IAudioTranscoderService, Pcm16AudioTranscoderService>();
            serviceCollection.TryAddTransient<IVoiceDataTranportService, UdpVoiceDataTransportService>();

            serviceCollection.AddResponder<VoiceStateUpdateResponder>();
            serviceCollection.AddResponder<VoiceServerUpdateResponder>();

            serviceCollection
                .Configure<JsonSerializerOptions>
                (
                    options =>
                    {
                        options.Converters.Add(new VoicePayloadConverter());

                        options
                            .AddVoiceGatewayCommandConverters()
                            .AddVoiceGatewayEventConverters();
                    }
                );

            return serviceCollection;
        }

        /// <summary>
        /// Adds the JSON converters that handle gateway command payloads.
        /// </summary>
        /// <param name="options">The serializer options.</param>
        /// <returns>The options, with the converters added.</returns>
        private static JsonSerializerOptions AddVoiceGatewayCommandConverters(this JsonSerializerOptions options)
        {
            // ConnectingResuming
            options.AddDataObjectConverter<IVoiceIdentify, VoiceIdentify>();
            options.AddDataObjectConverter<IVoiceResume, VoiceResume>();

            // Heartbeats
            options.AddDataObjectConverter<IVoiceHeartbeat, VoiceHeartbeat>();

            // Protocols
            options.AddDataObjectConverter<IVoiceProtocolData, VoiceProtocolData>();
            options.AddDataObjectConverter<IVoiceSelectProtocol, VoiceSelectProtocol>();
            options.AddDataObjectConverter<IVoiceSpeakingCommand, VoiceSpeakingCommand>();

            return options;
        }

        /// <summary>
        /// Adds the JSON converters that handle gateway event payloads.
        /// </summary>
        /// <param name="options">The serializer options.</param>
        /// <returns>The options, with the converters added.</returns>
        private static JsonSerializerOptions AddVoiceGatewayEventConverters(this JsonSerializerOptions options)
        {
            // ConnectingResuming
            options.AddDataObjectConverter<IVoiceHello, VoiceHello>()
                .WithPropertyName(v => v.Version, "v")
                .WithPropertyConverter(v => v.HeartbeatInterval, new UnitTimeSpanConverter(TimeUnit.Milliseconds));

            options.AddDataObjectConverter<IVoiceReady, VoiceReady>();

            // Heartbeats
            options.AddDataObjectConverter<IVoiceHeartbeatAcknowledge, VoiceHeartbeatAcknowledge>();

            // Sessions
            options.AddDataObjectConverter<IVoiceSessionDescription, VoiceSessionDescription>();

            // Clients
            options.AddDataObjectConverter<IVoiceClientConnect, VoiceClientConnect>();
            options.AddDataObjectConverter<IVoiceSpeakingEvent, VoiceSpeakingEvent>();
            options.AddDataObjectConverter<IVoiceClientDisconnect, VoiceClientDisconnect>();

            // Codecs
            options.AddDataObjectConverter<IVoiceCodecDescription, VoiceCodecDescription>();

            return options;
        }
    }
}
