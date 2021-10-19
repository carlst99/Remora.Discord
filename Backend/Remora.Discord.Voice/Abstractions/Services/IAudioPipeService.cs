//
//  IAudioPipeService.cs
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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Remora.Discord.Voice.Interop.Opus;
using Remora.Results;

namespace Remora.Discord.Voice.Abstractions.Services
{
    /// <summary>
    /// Represents an audio interface for encoding/decoding audio packets to/from a <see cref="IVoiceDataTranportService"/>.
    /// </summary>
    [PublicAPI]
    public interface IAudioPipeService
    {
        /// <summary>
        /// Initializes the service.
        /// </summary>
        /// <param name="audioType">The type of audio being transmitted, in order to optimize transmission.</param>
        /// <param name="voiceDataTransportService">The voice data transport service, used to send and receive audio.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        Result Initialize(OpusApplicationDefinition audioType, IVoiceDataTranportService voiceDataTransportService);

        /// <summary>
        /// Encodes sends a stream of audio.
        /// </summary>
        /// <param name="audioStream">The audio stream.</param>
        /// <param name="ct">A <see cref="CancellationToken"/> that can be used to stop the operation.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        Task<Result> SendAsync(Stream audioStream, CancellationToken ct = default);
    }
}
