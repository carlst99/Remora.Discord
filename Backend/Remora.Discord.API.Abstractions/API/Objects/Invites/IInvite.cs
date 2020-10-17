//
//  IInvite.cs
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

using Remora.Discord.Core;

namespace Remora.Discord.API.Abstractions.Objects
{
    /// <summary>
    /// Represents an invite.
    /// </summary>
    public interface IInvite
    {
        /// <summary>
        /// Gets the unique invite code.
        /// </summary>
        string Code { get; }

        /// <summary>
        /// Gets the guild this invite is for.
        /// </summary>
        Optional<IPartialGuild> Guild { get; }

        /// <summary>
        /// Gets the channel this invite is for.
        /// </summary>
        IPartialChannel Channel { get; }

        /// <summary>
        /// Gets the user who created the invite.
        /// </summary>
        Optional<IUser> Inviter { get; }

        /// <summary>
        /// Gets the target user for this invite.
        /// </summary>
        Optional<IPartialUser> TargetUser { get; }

        /// <summary>
        /// Gets the type of user target for this invite.
        /// </summary>
        Optional<TargetUserType> TargetUserType { get; }

        /// <summary>
        /// Gets the approximate count of online members. Only present when <see cref="TargetUser"/> is set.
        /// </summary>
        Optional<int> ApproximatePresenceCount { get; }

        /// <summary>
        /// Gets the approximate count of total members.
        /// </summary>
        Optional<int> ApproximateMemberCount { get; }
    }
}