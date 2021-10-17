//
//  OpusEncoder.cs
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

using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Remora.Discord.Voice.Errors;
using Remora.Results;

#pragma warning disable SA1300 // Element should begin with upper-case letter

namespace Remora.Discord.Voice.Interop.Opus
{
    /// <summary>
    /// Represents an interface to the native libsodium library.
    /// </summary>
    public sealed unsafe partial class OpusEncoder
    {
        private const string OpusLibraryName = "libopus";

        /// <summary>
        /// Encodes an opus frame.
        /// </summary>
        /// <param name="st">Encoder state.</param>
        /// <param name="pcm">Input signal (interleaved if 2 channels). length is frame_size * channels * sizeof(short).</param>
        /// <param name="frame_size">
        /// Number of samples per channel in the input signal. This must be an Opus frame size for the encoder's sampling rate.
        /// For example, at 48 kHz the permitted values are 120, 240, 480, 960, 1920, and 2880.
        /// Passing in a duration of less than 10 ms (480 samples at 48 kHz) will prevent the encoder from using the LPC or hybrid modes.
        /// </param>
        /// <param name="data">Output payload. This must contain storage for at least <paramref name="max_data_bytes"/>.</param>
        /// <param name="max_data_bytes">
        /// Size of the allocated memory for the output payload.
        /// This may be used to impose an upper limit on the instant bitrate, but should not be used as the only bitrate control.
        /// Use OPUS_SET_BITRATE to control the bitrate.
        /// </param>
        /// <returns>The length of the encoded packet (in bytes) on success or a negative error code (see <see cref="OpusErrorDefinition"/>) on failure.</returns>
        [DllImport(OpusLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_encode(IntPtr st, short* pcm, int frame_size, byte* data, int max_data_bytes);

        /// <summary>
        /// Allocates and initializes an encoder state.
        /// </summary>
        /// <param name="fs">Sampling rate of the input signal (Hz) This must be one of 8000, 12000, 16000, 24000, or 48000.</param>
        /// <param name="channels">Number of channels (1 or 2) in the input signal.</param>
        /// <param name="application">Coding mode (see <see cref="OpusApplicationDefinition"/>).</param>
        /// <param name="error">Error result, if any (see <see cref="OpusErrorDefinition"/>).</param>
        /// <returns>A pointer to the encoder state.</returns>
        [DllImport(OpusLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr opus_encoder_create(int fs, int channels, int application, out OpusErrorDefinition error);

        /// <summary>
        /// Gets a control parameter on an encoder.
        /// </summary>
        /// <param name="encoderState">A pointer to the encoder state.</param>
        /// <param name="request">The parameter to get the value of.</param>
        /// <param name="value">The value of the parameter.</param>
        /// <returns>An <see cref="OpusErrorDefinition"/> result.</returns>
        [DllImport(OpusLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_encoder_ctl")]
        private static extern OpusErrorDefinition _OpusEncoderGetCtl(IntPtr encoderState, OpusControlRequestDefinition request, out int value);

        /// <summary>
        /// Sets a control parameter on an encoder.
        /// </summary>
        /// <param name="encoderState">A pointer to the encoder state.</param>
        /// <param name="parameter">The parameter to set.</param>
        /// <param name="value">The value to set the parameter to.</param>
        /// <returns>An <see cref="OpusErrorDefinition"/> result.</returns>
        [DllImport(OpusLibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "opus_encoder_ctl")]
        private static extern OpusErrorDefinition _OpusEncoderSetCtl(IntPtr encoderState, OpusControlRequestDefinition parameter, int value);

        /// <summary>
        /// Frees an encoder allocation by <see cref="opus_encoder_create(int, int, int, out OpusErrorDefinition)"/>.
        /// </summary>
        /// <param name="st">A pointer to the encoder state.</param>
        [DllImport(OpusLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void opus_encoder_destroy(IntPtr st);
    }

    [PublicAPI]
    public sealed partial class OpusEncoder : IDisposable
    {
        private const int DiscordSampleRate = 48000;
        private const int DiscordChannelCount = 2;

        /// <summary>
        /// Gets the maximum allowed bitrate in a Discord channel. Currently 128kbps.
        /// </summary>
        public const int DiscordMaxBitrate = 131072;

        private readonly IntPtr _state;

        /// <summary>
        /// Gets a value indicating whether or not this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        private OpusEncoder(IntPtr state)
        {
            _state = state;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpusEncoder"/> class.
        /// </summary>
        /// <param name="codingMode">The coding mode to use.</param>
        /// <returns>The created <see cref="OpusEncoder"/>, or an error if the initialization failed.</returns>
        public static Result<OpusEncoder> Create(OpusApplicationDefinition codingMode)
        {
            IntPtr state = opus_encoder_create(DiscordSampleRate, DiscordChannelCount, (int)codingMode, out OpusErrorDefinition error);

            if (error is not OpusErrorDefinition.OK)
            {
                return new OpusError(error, "Failed to create an encoder.");
            }

            error = codingMode switch
            {
                OpusApplicationDefinition.Voip => _OpusEncoderSetCtl(state, OpusControlRequestDefinition.SetSignal, (int)OpusSignalDefinition.Voice),
                OpusApplicationDefinition.Audio => _OpusEncoderSetCtl(state, OpusControlRequestDefinition.SetSignal, (int)OpusSignalDefinition.Music),
                _ => _OpusEncoderSetCtl(state, OpusControlRequestDefinition.SetSignal, (int)OpusSpecialDefinition.Auto),
            };

            if (error is not OpusErrorDefinition.OK)
            {
                opus_encoder_destroy(state);
                return new OpusError(error, "Failed to set encoder signal type");
            }

            error = _OpusEncoderSetCtl(state, OpusControlRequestDefinition.SetPacketLossPercentage, 15);
            if (error is not OpusErrorDefinition.OK)
            {
                opus_encoder_destroy(state);
                return new OpusError(error, "Failed to set expected packet loss on encoder.");
            }

            error = _OpusEncoderSetCtl(state, OpusControlRequestDefinition.SetInbandFec, 1); // Enable
            if (error is not OpusErrorDefinition.OK)
            {
                opus_encoder_destroy(state);
                return new OpusError(error, "Failed to enable in-band forward error control on encoder.");
            }

            error = _OpusEncoderSetCtl(state, OpusControlRequestDefinition.SetBitrate, DiscordMaxBitrate); // 128 kbps, maximum bitrate in a Discord channel.
            if (error is not OpusErrorDefinition.OK)
            {
                opus_encoder_destroy(state);
                return new OpusError(error, "Failed to set bitrate on encoder.");
            }

            return new OpusEncoder(state);
        }

        /// <summary>
        /// Encodes an audio sample.
        /// </summary>
        /// <param name="pcm16">The PCM-16 audio data to encode.</param>
        /// <param name="output">The output buffer. Must be the same length as the <paramref name="pcm16"/> buffer.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        public unsafe Result Encode(ReadOnlySpan<short> pcm16, Span<byte> output)
        {
            if (pcm16.Length != output.Length)
            {
                return new ArgumentOutOfRangeError(nameof(output), "PCM and output buffers must be of equal length.");
            }

            int sampleDurationMS = pcm16.Length / (DiscordSampleRate / 1000) / DiscordChannelCount;
            int frameSize = sampleDurationMS * (DiscordSampleRate / 1000);

            int writtenLength;
            fixed (short* pcm16Ptr = pcm16)
            {
                fixed (byte* outputPtr = output)
                {
                    writtenLength = opus_encode(_state, pcm16Ptr, frameSize, outputPtr, output.Length);
                }
            }

            return writtenLength < 0
                ? new OpusError((OpusErrorDefinition)writtenLength, "Failed to encode audio sample.")
                : Result.FromSuccess();
        }

        /// <summary>
        /// Sets the bitrate to encode at.
        /// </summary>
        /// <param name="bitrate">The new bitrate. Must be between 0 and <see cref="DiscordMaxBitrate"/>.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        public Result SetBitrate(int bitrate)
        {
            if (bitrate < 0 || bitrate > DiscordMaxBitrate)
            {
                return new ArgumentOutOfRangeError(nameof(bitrate), $"Bitrate must be greater than zero and less than {DiscordMaxBitrate}.");
            }

            OpusErrorDefinition error = _OpusEncoderSetCtl(_state, OpusControlRequestDefinition.SetBitrate, bitrate);
            return error is not OpusErrorDefinition.OK
                ? new OpusError(error, "Failed to set bitrate on encoder.")
                : Result.FromSuccess();
        }

        /// <summary>
        /// Resets the state of the encoder in preparation for submitting a new stream.
        /// </summary>
        /// <returns>A result representing the outcome of the operation.</returns>
        public Result Reset()
        {
            OpusErrorDefinition error = _OpusEncoderSetCtl(_state, OpusControlRequestDefinition.ResetState, 0);
            return error is not OpusErrorDefinition.OK
                ? new OpusError(error, "Failed to reset the encoder.")
                : Result.FromSuccess();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                opus_encoder_destroy(_state);

                IsDisposed = true;
            }
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="OpusEncoder"/> class.
        /// </summary>
        ~OpusEncoder()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }
    }
}
