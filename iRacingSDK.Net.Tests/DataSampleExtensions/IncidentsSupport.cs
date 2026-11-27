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

using NUnit.Framework;
using System.Linq;
using System.Collections.Generic;

namespace iRacingSDK.Net.Tests
{
    [TestFixture]
    public class IncidentsSupport
    {
        private List<DataSample> samples;
        private List<short> requestedCars;
        private SessionData sessionData;

        IncidentsSupport BuildSamples(bool hasPaceCar = true)
        {
            samples = new List<DataSample>();
            requestedCars = new List<short>();
            sessionData = new SessionData { DriverInfo = new SessionData._DriverInfo() };
            var driver = new SessionData._DriverInfo._Drivers { CarIdx = 0, CarNumberRaw = 1 };
            var paceCar = new SessionData._DriverInfo._Drivers { CarIdx = 62, CarNumberRaw = 55, CarIsPaceCar = 1 };
            sessionData.DriverInfo.Drivers = hasPaceCar ? new[] { driver, paceCar } : new[] { driver };

            return this;
        }

        List<DataSample> Scan()
        {
            return iRacingSDK.IncidentsSupport.FindIncidents(samples, 3, int.MaxValue, requestedCars.Add, () => { });
        }

        IncidentsSupport OnACar(int countOfSamples = 1)
        {
            return OnCamera(0, countOfSamples);
        }

        IncidentsSupport OnAPaceCar(int countOfSamples = 1)
        {
            return OnCamera(62, countOfSamples);
        }

        IncidentsSupport OnCamera(int carIdx, int countOfSamples)
        {
            int frameNumber = samples.Count;
            for (var i = 0; i < countOfSamples; i++)
                samples.Add(new DataSample
                {
                    IsConnected = true,
                    SessionData = sessionData,
                    Telemetry = new Telemetry
                        {
                            { "CamCarIdx", carIdx },
                            { "ReplayFrameNum", frameNumber }
                        }
                });

            return this;
        }

        int[] FrameNumbersFromSamples(IEnumerable<DataSample> samples)
        {
            return samples.Select(data => data.Telemetry.ReplayFrameNum).ToArray();
        }

        //Simulate a series of samples, representing a paused replay
        //that jumps to another incident's frame number 

        [Test]
        public void should_find_single_incidents()
        {
            var samples = BuildSamples().OnACar().OnAPaceCar().OnACar(3).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 2 }));
            Assert.That(requestedCars, Is.EqualTo(new[] { (short)55 }));

        }

        [Test]
        public void should_find_two_incidents()
        {
            var samples = BuildSamples().OnACar().OnAPaceCar().OnACar(4).OnAPaceCar().OnACar(4).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 2, 7 }));
        }
        
        [Test]
        public void should_stop_if_frame_number_does_not_advance()
        {
            var samples = BuildSamples().OnACar().OnAPaceCar().OnACar(4).OnAPaceCar(10).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void should_scan_without_a_pace_car()
        {
            var samples = BuildSamples(hasPaceCar: false).OnACar().OnACar(3).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 1 }));
            Assert.That(requestedCars, Is.Empty);
        }

        [Test]
        public void should_continue_after_a_marker_on_the_pace_car()
        {
            var samples = BuildSamples().OnACar().OnAPaceCar().OnAPaceCar(4).OnACar(3).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 6 }));
        }

        [Test]
        public void should_wait_for_a_seek_that_keeps_reporting_the_previous_frame()
        {
            var samples = BuildSamples(hasPaceCar: false).OnACar(100).OnACar(3).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 100 }));
        }

        [Test]
        public void should_scan_if_the_pace_car_camera_does_not_respond()
        {
            var samples = BuildSamples().OnACar(121).OnACar(3).Scan();

            Assert.That(FrameNumbersFromSamples(samples), Is.EqualTo(new[] { 121 }));
        }
    }
}
