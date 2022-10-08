//
//  GatewayBidirectionalTestBase.cs
//
//  Author:
//       Jarl Gullberg <jarl.gullberg@gmail.com>
//
//  Copyright (c) Jarl Gullberg
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

using Remora.Discord.API.Abstractions.Gateway.Commands;
using Remora.Discord.API.Abstractions.Gateway.Events;
using Remora.Discord.API.Tests.Services;

namespace Remora.Discord.API.Tests.TestBases;

/// <summary>
/// Acts as a base class for command API types.
/// </summary>
/// <typeparam name="TType">The type under test.</typeparam>
public abstract class GatewayBidirectionalTestBase<TType> : GatewayTestBase<TType, SampleBidirectionalDataSource<TType>>
    where TType : IGatewayEvent, IGatewayCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayBidirectionalTestBase{TType}"/> class.
    /// </summary>
    /// <param name="fixture">The test fixture.</param>
    protected GatewayBidirectionalTestBase(JsonBackedTypeTestFixture fixture)
        : base(fixture)
    {
    }
}
