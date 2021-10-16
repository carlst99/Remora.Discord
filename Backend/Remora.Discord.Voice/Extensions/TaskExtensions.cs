//
//  TaskExtensions.cs
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

using Remora.Results;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Defines extension methods for the <see cref="Task"/> interface.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Throws a <see cref="TaskCanceledException"/> if a task does not complete before the given timeout.
        /// </summary>
        /// <typeparam name="T">The return type of the task.</typeparam>
        /// <param name="task">The task that should be completed before the timeout.</param>
        /// <param name="timeout">The timeout.</param>
        /// <param name="ct">A <see cref="CancellationToken"/> used to stop the timeout early.</param>
        /// <returns>The result of the task if it returned before the timeout, else an error containing a <see cref="TaskCanceledException"/>.</returns>
        public static async Task<Result<T>> WithTimeout<T>(this Task<T> task, TimeSpan timeout, CancellationToken ct = default)
        {
            Task timeoutTask = Task.Delay(timeout, ct);

            if (task != await Task.WhenAny(task, timeoutTask).ConfigureAwait(false))
            {
                return new TaskCanceledException();
            }

            return await task.ConfigureAwait(false);
        }
    }
}
