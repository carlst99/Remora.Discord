//
//  Sodium.cs
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

#pragma warning disable SA1300 // Element should begin with upper-case letter

namespace Remora.Discord.Voice.Interop
{
    /// <summary>
    /// Represents an interface to the native libsodium library.
    /// </summary>
    public static class Sodium
    {
        /// <summary>
        /// Gets the version string of the sodium library.
        /// </summary>
        /// <returns>The sodium library version.</returns>
        public static string? GetVersionString()
            => Marshal.PtrToStringAnsi(sodium_version_string());

        [DllImport("libsodium", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sodium_version_string();
    }
}
