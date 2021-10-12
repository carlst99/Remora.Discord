//
//  Payload.cs
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

#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System.Collections.Generic;

namespace Remora.Discord.Benchmarks.Data
{
    public record Payload(int Number, List<string> StringList)
    {
        public static Payload LessThan4096()
            => Generate(500);

        public static Payload MoreThan4096()
            => Generate(1000);

        private static Payload Generate(int stringCount)
        {
            List<string> strings = new();

            for (int i = 0; i < stringCount; i++)
            {
                strings.Add(i.ToString());
            }

            return new Payload(int.MaxValue, strings);
        }
    }
}
