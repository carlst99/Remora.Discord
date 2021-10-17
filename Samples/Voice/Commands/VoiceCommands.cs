﻿//
//  VoiceCommands.cs
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

using System.Threading.Tasks;
using Remora.Commands.Attributes;
using Remora.Commands.Groups;
using Remora.Discord.API.Abstractions.Objects;
using Remora.Discord.Commands.Attributes;
using Remora.Discord.Commands.Conditions;
using Remora.Discord.Commands.Contexts;
using Remora.Discord.Commands.Feedback.Services;
using Remora.Discord.Voice;
using Remora.Discord.Voice.Interop;
using Remora.Discord.Voice.Interop.Opus;
using Remora.Results;

namespace Remora.Discord.Samples.Caching.Commands
{
    /// <summary>
    /// Contains commands for controlling voice connections.
    /// </summary>
    [RequireContext(ChannelType.GuildText)]
    public class VoiceCommands : CommandGroup
    {
        private readonly ICommandContext _context;
        private readonly DiscordVoiceClientFactory _voiceClientFactory;
        private readonly FeedbackService _feedbackService;

        /// <summary>
        /// Initializes a new instance of the <see cref="VoiceCommands"/> class.
        /// </summary>
        /// <param name="context">The command context.</param>
        /// <param name="voiceClientFactory">The voice client factory.</param>
        /// <param name="feedbackService">The feedback service.</param>
        public VoiceCommands
        (
            ICommandContext context,
            DiscordVoiceClientFactory voiceClientFactory,
            FeedbackService feedbackService
        )
        {
            _context = context;
            _voiceClientFactory = voiceClientFactory;
            _feedbackService = feedbackService;
        }

        /// <summary>
        /// Connects a voice client.
        /// </summary>
        /// <param name="connectTo">The channel to connect to.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        [Command("connect")]
        public async Task<IResult> ConnectCommandAsync
        (
            [ChannelTypes(ChannelType.GuildVoice)] IChannel connectTo
        )
        {
            DiscordVoiceClient client = _voiceClientFactory.Get(_context.GuildID.Value);

            Result runResult = await client.RunAsync
            (
                _context.GuildID.Value,
                connectTo.ID,
                true,
                false,
                OpusApplicationDefinition.Audio,
                CancellationToken
            ).ConfigureAwait(false);

            if (!runResult.IsSuccess)
            {
                return await _feedbackService.SendContextualErrorAsync
                (
                    $"Failed to start a voice session: {runResult.Error}",
                    ct: CancellationToken
                );
            }

            return await _feedbackService.SendContextualSuccessAsync("Connected!", ct: CancellationToken);
        }

        /// <summary>
        /// Disconnects a voice client.
        /// </summary>
        /// <returns>A result representing the outcome of the operation.</returns>
        [Command("disconnect")]
        public async Task<IResult> DisconnectCommandAsync()
        {
            DiscordVoiceClient client = _voiceClientFactory.Get(_context.GuildID.Value);

            Result stopResult = await client.StopAsync().ConfigureAwait(false);
            if (!stopResult.IsSuccess)
            {
                return await _feedbackService.SendContextualErrorAsync
                (
                    $"Failed to stop a voice session: {stopResult.Error}",
                    ct: CancellationToken
                );
            }

            return await _feedbackService.SendContextualSuccessAsync("Disconnected!", ct: CancellationToken);
        }

        /// <summary>
        /// Gets the version of the underlying sodium library.
        /// </summary>
        /// <returns>A result representing the outcome of the operation.</returns>
        [Command("sodium-info")]
        public async Task<IResult> SodiumVersionCommandAsync()
        {
            return await _feedbackService.SendContextualInfoAsync
            (
                $"Version: {Sodium.Version ?? "Unknown"}" +
                $"\nNonce Size: {Sodium.NonceSize}" +
                $"\nMAC Size: {Sodium.MacSize}",
                ct: CancellationToken
            );
        }
    }
}
