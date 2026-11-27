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

using iRacingSDK.Support;
using System;
using System.Collections.Generic;
using System.Linq;

namespace iRacingSDK
{
    public static class IncidentsSupport
    {
        /// <summary>
        /// Pauses the replay and seeks forward through incident markers, collecting one sample per incident.
        /// </summary>
        /// <param name="samples"></param>
        /// <param name="sampleScanSettle">The number of samples to take, which must all have the same frame number - to identify that iRacing has paused.</param>
        /// <param name="maxTotalIncidents">an optional hard limit on the total number of incidents that can be returned.</param>
        /// <returns>DataSamples of each incident</returns>
        public static List<DataSample> FindIncidents(IEnumerable<DataSample> samples, int sampleScanSettle, int maxTotalIncidents = int.MaxValue)
        {
            if (maxTotalIncidents <= 0)
                return new List<DataSample>();

            iRacing.Replay.SetSpeed(0);
            iRacing.Replay.Wait();

            var replay = iRacing.Replay.NoWait;
            return FindIncidents(samples, sampleScanSettle, maxTotalIncidents,
                carNumber => replay.CameraOnDriver(carNumber, 0), replay.MoveToNextIncident);
        }

        internal static List<DataSample> FindIncidents(IEnumerable<DataSample> samples, int sampleScanSettle,
            int maxTotalIncidents, Action<short> selectCamera, Action nextIncident)
        {
            return PositionsOf(samples, Math.Max(2, sampleScanSettle), selectCamera, nextIncident)
                .Take(maxTotalIncidents).ToList();
        }

        private static IEnumerable<DataSample> PositionsOf(IEnumerable<DataSample> samples, int sampleScanSettle,
            Action<short> selectCamera, Action nextIncident)
        {
            using (var feed = samples.GetEnumerator())
            {
                while (feed.MoveNext() && feed.Current.IsConnected)
                {
                    var sample = feed.Current;
                    var paceCar = sample.SessionData.DriverInfo.PaceCar;
                    var tickRate = Math.Max(60, sample.Telemetry.TickRate);
                    if (paceCar != null && sample.Telemetry.CamCarIdx != paceCar.CarIdx)
                    {
                        selectCamera((short)paceCar.CarNumberRaw);
                        var cameraWait = 2 * tickRate;
                        while (cameraWait-- > 0 && sample.Telemetry.CamCarIdx != paceCar.CarIdx)
                        {
                            if (!feed.MoveNext() || !feed.Current.IsConnected)
                                yield break;
                            sample = feed.Current;
                        }
                        if (sample.Telemetry.CamCarIdx != paceCar.CarIdx)
                            TraceWarning.WriteLine("Pace-car camera did not respond. Searching for the next incident from the current camera.");
                    }

                    var startFrame = sample.Telemetry.ReplayFrameNum;
                    var lastFrame = startFrame;
                    var lastCamera = sample.Telemetry.CamCarIdx;
                    var settled = 0;
                    var seekWait = Math.Max(5 * tickRate, 2 * sampleScanSettle);
                    nextIncident();
                    while (settled < sampleScanSettle && seekWait-- > 0)
                    {
                        if (!feed.MoveNext() || !feed.Current.IsConnected)
                            yield break;
                        sample = feed.Current;
                        var frame = sample.Telemetry.ReplayFrameNum;
                        var camera = sample.Telemetry.CamCarIdx;
                        settled = frame <= startFrame ? 0 : frame == lastFrame && camera == lastCamera ? settled + 1 : 1;
                        lastFrame = frame;
                        lastCamera = camera;
                    }

                    if (settled < sampleScanSettle)
                    {
                        if (lastFrame <= startFrame)
                            TraceDebug.WriteLine("No further incident marker was found after frame {0}.", startFrame);
                        else
                            TraceWarning.WriteLine("Incident scan stopped because the replay did not settle after the seek.");
                        yield break;
                    }
                    if (paceCar != null && lastCamera == paceCar.CarIdx)
                    {
                        TraceDebug.WriteLine("Skipping the pace-car incident marker at frame {0}.", lastFrame);
                        continue;
                    }
                    if (!sample.SessionData.DriverInfo.Drivers.Any(driver => driver.CarIdx == lastCamera && driver.IsRaceDriver))
                    {
                        TraceDebug.WriteLine("Skipping the incident marker at frame {0} without a race car in view.", lastFrame);
                        continue;
                    }

                    TraceDebug.WriteLine("Incident scan settled on frame {0}", lastFrame);
                    yield return sample;
                }
            }
        }
    }
}
