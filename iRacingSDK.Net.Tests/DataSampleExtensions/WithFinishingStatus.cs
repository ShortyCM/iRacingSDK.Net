using System.Linq;
using NUnit.Framework;

namespace iRacingSDK.Net.Tests
{
    [TestFixture]
    public class WithFinishingStatus
    {
        [Test]
        public void MarksCarZeroAsFinishedAndKeepsTheFlagAfterTheCrossing()
        {
            var session = CreateSession();
            var previous = CreateSample(session);
            previous.Telemetry.CarIdxLapDistPct[0] = 0.99f;
            previous.Telemetry.CarIdxLapDistPct[1] = 0.99f;
            previous.Telemetry.CarIdxLapDistPct[2] = 0.99f;
            var crossing = CreateSample(session, previous);
            crossing.Telemetry.CarIdxLapDistPct[0] = 0.01f;
            crossing.Telemetry.CarIdxLapDistPct[1] = 0.01f;
            crossing.Telemetry.CarIdxLapDistPct[2] = -1;
            var afterward = CreateSample(session, crossing);
            afterward.Telemetry.CarIdxLapDistPct[0] = 0.25f;

            foreach (var sample in new[] { crossing, afterward }.WithFinishingStatus())
            {
                Assert.That(sample.Telemetry.Cars[0].HasSeenCheckeredFlag, Is.True);
                Assert.That(sample.Telemetry.Cars[1].HasSeenCheckeredFlag, Is.False);
                Assert.That(sample.Telemetry.Cars[2].HasSeenCheckeredFlag, Is.False);
            }
        }

        [Test]
        public void MarksCarZeroAsRetiredIfItLeavesTheReplayWithoutCrossing()
        {
            var sample = CreateSample(CreateSession());
            sample.Telemetry.CarIdxTrackSurface[0] = TrackLocation.NotInWorld;

            var processed = new[] { sample }.WithFinishingStatus().Single();

            Assert.That(processed.Telemetry.Cars[0].HasRetired, Is.True);
            Assert.That(processed.Telemetry.Cars[0].HasSeenCheckeredFlag, Is.False);
        }

        private static SessionData CreateSession()
        {
            return new SessionData
            {
                DriverInfo = new SessionData._DriverInfo
                {
                    Drivers = new[]
                    {
                        new SessionData._DriverInfo._Drivers { CarIdx = 0, CarNumberRaw = 1 },
                        new SessionData._DriverInfo._Drivers { CarIdx = 1, CarIsPaceCar = 1 },
                        new SessionData._DriverInfo._Drivers { CarIdx = 2, CarNumberRaw = 4 }
                    }
                },
                SessionInfo = new SessionData._SessionInfo
                {
                    Sessions = new[] { new SessionData._SessionInfo._Sessions { ResultsLapsComplete = 6 } }
                }
            };
        }

        private static DataSample CreateSample(SessionData session, DataSample previous = null)
        {
            var telemetry = new Telemetry
            {
                { "RaceLaps", 7 },
                { "SessionNum", 0 },
                { "SessionState", SessionState.Checkered },
                { "SessionTime", 90d },
                { "CarIdxLapDistPct", new float[64] },
                { "CarIdxTrackSurface", new TrackLocation[64] }
            };
            telemetry.SessionData = session;
            telemetry.CarIdxTrackSurface[0] = TrackLocation.OnTrack;
            telemetry.CarIdxTrackSurface[2] = TrackLocation.OnTrack;
            return new DataSample { IsConnected = true, SessionData = session, Telemetry = telemetry, LastSample = previous };
        }
    }
}
