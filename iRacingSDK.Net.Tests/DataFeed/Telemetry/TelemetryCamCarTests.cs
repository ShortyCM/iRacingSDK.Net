using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace iRacingSDK
{
    [TestFixture]
    public class TelemetryCamCarTests
    {
        SessionData._DriverInfo._Drivers driver;
        Telemetry telemetry;
        
        [SetUp]
        public void setup()
        {
            telemetry = new Telemetry();

            driver = new SessionData._DriverInfo._Drivers { CarNumberRaw = 1 };

            telemetry.SessionData = new SessionData
            {
                DriverInfo = new SessionData._DriverInfo
                {
                    Drivers = new SessionData._DriverInfo._Drivers[] 
                    {
                        driver
                    }
                }
            };
        }

        [Test]
        public void ShouldReturnCarByIndex()
        {
            var c = telemetry.Cars[0];

            Assert.That(c.CarIdx, Is.EqualTo(0));
        }

        [Test]
        public void ShouldRaiseFriendErrorWhenAccessUnknownCarIdx()
        {
            Assert.Throws(Is.TypeOf<Exception>().And.Message.EqualTo("Attempt to load car details for unknown carIndex.  carIdx: 5, maxNumber: 0"), () => { var a = telemetry.Cars[5]; });
        }

        [Test]
        public void ShouldResolvePaceCarOutsideTheCompetitorIndices()
        {
            telemetry.SessionData.DriverInfo.Drivers = new[]
            {
                driver, new SessionData._DriverInfo._Drivers { CarIdx = 62, CarIsPaceCar = 1 }
            };
            telemetry.Add("CamCarIdx", 62);
            var surface = new TrackLocation[64];
            surface[0] = TrackLocation.OnTrack;
            telemetry.Add("CarIdxTrackSurface", surface);

            Assert.That(telemetry.CamCar.CarIdx, Is.EqualTo(62));
            Assert.That(telemetry.CamCar.Details.IsPaceCar, Is.True);
            Assert.That(telemetry.Cars[0].Details.IsPaceCar, Is.False);
            Assert.That(telemetry.UnderPaceCar, Is.False);
            surface[62] = TrackLocation.OnTrack;
            Assert.That(telemetry.UnderPaceCar, Is.True);
        }

        [Test]
        public void ShouldRankTheDriverAtIndexZeroAheadOfThePaceCarAndSpectators()
        {
            telemetry.SessionData.DriverInfo.Drivers = new[]
            {
                driver,
                new SessionData._DriverInfo._Drivers { CarIdx = 2, CarNumber = "0" },
                new SessionData._DriverInfo._Drivers { CarIdx = 3, CarNumberRaw = 5, IsSpectator = 1 },
                new SessionData._DriverInfo._Drivers { CarIdx = 62, CarIsPaceCar = 1 }
            };
            telemetry.Add("CamCarIdx", 62);
            telemetry.CarIdxDistance = new float[64];
            telemetry.CarIdxDistance[0] = 2.5f;
            telemetry.CarIdxDistance[1] = 5f;
            telemetry.CarIdxDistance[2] = 2f;
            telemetry.CarIdxDistance[3] = 6f;
            telemetry.CarIdxDistance[62] = 7f;

            Assert.That(telemetry.RaceCars.Select(car => car.CarIdx), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(telemetry.Cars[0].Position, Is.EqualTo(1));
            Assert.That(telemetry.Cars[2].Position, Is.EqualTo(2));
            Assert.That(telemetry.CamCar.Position, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ShouldUseReportedStartingOrderBeforeCarsHavePositiveLapDistances()
        {
            telemetry.SessionData.DriverInfo.Drivers = new[]
            {
                driver, new SessionData._DriverInfo._Drivers { CarIdx = 4, CarNumberRaw = 8 }
            };
            telemetry.CarIdxDistance = new float[64];
            var reportedPositions = new int[64];
            reportedPositions[0] = 2;
            reportedPositions[4] = 1;
            telemetry.Add("CarIdxPosition", reportedPositions);

            Assert.That(telemetry.Cars[4].Position, Is.EqualTo(1));
            Assert.That(telemetry.Cars[0].Position, Is.EqualTo(2));
        }

        [Test]
        public void ShouldReadPitRoadStatusForEachCar()
        {
            telemetry.SessionData.DriverInfo.Drivers = new[]
            {
                driver, new SessionData._DriverInfo._Drivers { CarIdx = 1, CarNumberRaw = 2 }
            };
            telemetry.Add("OnPitRoad", false);
            telemetry.Add("CarIdxOnPitRoad", new[] { false, true });

            Assert.That(telemetry.Cars[0].Details.IsOnPitRoad, Is.False);
            Assert.That(telemetry.Cars[1].Details.IsOnPitRoad, Is.True);

            telemetry.Remove("CarIdxOnPitRoad");
            telemetry["OnPitRoad"] = true;
            Assert.That(telemetry.Cars[0].Details.IsOnPitRoad, Is.True);
            Assert.That(telemetry.Cars[1].Details.IsOnPitRoad, Is.False);
        }
    }
}
