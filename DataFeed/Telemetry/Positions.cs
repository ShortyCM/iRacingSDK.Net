// This file is part of iRacingSDK.
//
// Copyright 2014 Dean Netherton
// https://github.com/vipoo/iRacingSDK.Net
//
// iRacingSDK is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// iRacingSDK is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with iRacingSDK.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;

namespace iRacingSDK
{
	public partial class Telemetry : Dictionary<string, object>
	{
        int[] positions;
        public int[] Positions
        {
            get
            {
                if (positions != null)
                    return positions;

                var drivers = SessionData.DriverInfo.CompetingDrivers;
                var distances = CarIdxDistance;
                object reported;
                TryGetValue("CarIdxPosition", out reported);
                var official = reported as int[] ?? new int[0];

                var runningOrder = Enumerable.Range(0, drivers.Length)
                    .Where(index => drivers[index].IsRaceDriver)
                    .OrderByDescending(index => index < distances.Length && distances[index] > 0 ? distances[index] : 0)
                    .ThenBy(index => index < official.Length && official[index] > 0 ? official[index] : int.MaxValue);

                positions = Enumerable.Repeat(int.MaxValue, Math.Max(drivers.Length, distances.Length)).ToArray();
                var position = 1;
                foreach (var index in runningOrder)
                    positions[index] = position++;

                return positions;
            }
        }
	}
}
