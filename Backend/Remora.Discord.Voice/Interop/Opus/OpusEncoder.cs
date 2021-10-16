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
        private static extern IntPtr opus_encoder_create(int fs, int channels, int application, out int error);

        // TODO: opus_encoder_ctl

        /// <summary>
        /// Frees an encoder allocation by <see cref="opus_encoder_create(int, int, int, out int)"/>.
        /// </summary>
        /// <param name="st">The encoder state.</param>
        [DllImport(OpusLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void opus_encoder_destroy(IntPtr st);
    }

    [PublicAPI]
    public sealed partial class OpusEncoder : IDisposable
    {
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
        /// Creates a new instance of the <see cref="OpusEncoder"/> class.
        /// </summary>
        /// <param name="codingMode">The coding mode to use.</param>
        /// <returns>A result representing the outcome of the operation.</returns>
        public static Result<OpusEncoder> Create(OpusApplicationDefinition codingMode)
        {
            IntPtr state = opus_encoder_create(48000, 2, (int)codingMode, out int error);

            if (!Enum.IsDefined(typeof(OpusErrorDefinition), error))
            {
                return new OpusError((OpusErrorDefinition)int.MinValue, "An unknown (non-opus defined) error occured.");
            }

            OpusErrorDefinition errorDefinition = (OpusErrorDefinition)error;
            if (errorDefinition is not OpusErrorDefinition.OK)
            {
                return new OpusError(errorDefinition, "Failed to create an encoder.");
            }

            return new OpusEncoder(state);
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
