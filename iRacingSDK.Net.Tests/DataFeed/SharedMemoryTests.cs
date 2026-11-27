using System;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace iRacingSDK
{
    [TestFixture]
    public class SharedMemoryTests
    {
        const int VariableHeaders = 128;
        const int VarHeaderSize = 144;
        const int FirstRow = 512;
        const int SecondRow = 544;
        const int SessionInfo = 768;

        MemoryMappedFile mapping;
        MemoryMappedViewAccessor view;
        DataFeed reader;
        string mappingName;

        [SetUp]
        public void SetUp()
        {
            mappingName = "Local\\iRacingSDK.Net.Tests-" + Guid.NewGuid().ToString("N");
            mapping = MemoryMappedFile.CreateNew(mappingName, 4096);
            view = mapping.CreateViewAccessor();
            reader = new DataFeed(view);

            // The SDK 1.20 example writer still sets ver to 1.
            view.Write(0, 1);
            view.Write(4, 1);
            view.Write(8, 360);
            view.Write(24, 2);
            view.Write(28, VariableHeaders);
            view.Write(32, 2);
            view.Write(36, 32);
            WriteBuffer(48, 20, FirstRow, 20);
            WriteBuffer(64, 19, SecondRow, 19);
            view.Write(40, 20);
            view.Write(FirstRow, 20);
            view.Write(SecondRow, 19);
            WriteVariable(VariableHeaders, VarType.irInt, 0, 1, "Sequence");
            WriteVariable(VariableHeaders + VarHeaderSize, VarType.irFloat, 4, 2, "ControlHistory");
            view.Write(VariableHeaders + VarHeaderSize + 12, (byte)1);
            view.WriteArray(FirstRow + 4, new[] { 1.25f, 2.5f }, 0, 2);
            WriteSession(Encoding.UTF8, "  Encoding: UTF8\n", true);
        }

        [TearDown]
        public void TearDown()
        {
            view.Dispose();
            mapping.Dispose();
        }

        [Test]
        public void RejectsRowsWhileTheProducerIsOverwritingThem()
        {
            view.Write(56, 21);
            view.Write(FirstRow, 21);
            Assert.That(reader.GetNextDataSample(20, false), Is.Null);

            view.Write(48, 21);
            view.Write(40, 21);
            var data = reader.GetNextDataSample(21, false);
            Assert.That(data.Telemetry.TickCount, Is.EqualTo(21));
            Assert.That(data.Telemetry["Sequence"], Is.EqualTo(21));
        }

        [Test]
        public void KeepsRequestedRowsAndUsesThePublishedIndexForFallback()
        {
            view.Write(40, 19);
            view.Write(44, (byte)1);

            Assert.That(reader.GetNextDataSample(20, false).Telemetry.TickCount, Is.EqualTo(20));
            Assert.That(reader.GetNextDataSample(22, false).Telemetry.TickCount, Is.EqualTo(19));
        }

        [Test]
        public void ReadsLegacyBuffersWithZeroedPadding()
        {
            view.Write(40, 0);
            view.Write(56, 0);
            view.Write(72, 0);
            Assert.That(reader.GetNextDataSample(22, false).Telemetry.TickCount, Is.EqualTo(20));
        }

        [Test]
        public void ReadsUtf8AndExposesTheTickRateAndTimeSeriesMetadata()
        {
            var data = reader.GetNextDataSample(20, false);

            Assert.That(data.SessionData.DriverInfo.Drivers[0].UserName, Is.EqualTo("Jos\u00E9"));
            Assert.That(data.Telemetry.TickRate, Is.EqualTo(360));
            Assert.That(data.Telemetry.TimeSeriesVariables, Is.EqualTo(new[] { "ControlHistory" }));
            Assert.That(data.Telemetry["ControlHistory"], Is.EqualTo(new[] { 1.25f, 2.5f }));
        }

        [Test]
        public void ReadsLegacySessionTextAndNullTerminatedCharacterVariables()
        {
            WriteSession(Encoding.GetEncoding(28591), "", false);
            WriteVariable(VariableHeaders, VarType.irChar, 0, 4, "Label");
            WriteBytes(FirstRow, new byte[] { (byte)'O', (byte)'K', 0, (byte)'X' });

            var data = reader.GetNextDataSample(20, false);

            Assert.That(data.SessionData.DriverInfo.Drivers[0].UserName, Is.EqualTo("Jos\u00E9"));
            Assert.That(data.Telemetry["Label"], Is.EqualTo("OK"));
        }

        [Test]
        public void PublicFeedReportsDisconnectAndClosesItsReadOnlyView()
        {
            var eventName = mappingName + "-ready";
            using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset, eventName))
            using (var memory = new iRacingMemory(mappingName, eventName))
            {
                var connection = new iRacingConnection(memory);
                using (var samples = connection.GetDataFeed(false).GetEnumerator())
                {
                    Assert.That(samples.MoveNext(), Is.True);
                    Assert.That(samples.Current.IsConnected, Is.True);
                    Assert.That(samples.Current.Telemetry.TickCount, Is.EqualTo(20));
                    Assert.That(memory.Accessor.CanWrite, Is.False);

                    view.Write(4, 0);
                    Assert.That(samples.MoveNext(), Is.True);
                    Assert.That(samples.Current.IsConnected, Is.False);
                }

                Assert.That(memory.Accessor, Is.Null);
            }
        }

        void WriteBuffer(int descriptor, int tick, int offset, int writeTick)
        {
            view.Write(descriptor, tick);
            view.Write(descriptor + 4, offset);
            view.Write(descriptor + 8, writeTick);
        }

        void WriteVariable(int descriptor, VarType type, int offset, int count, string name)
        {
            view.Write(descriptor, (int)type);
            view.Write(descriptor + 4, offset);
            view.Write(descriptor + 8, count);
            WriteBytes(descriptor + 16, Encoding.ASCII.GetBytes(name + "\0"));
        }

        void WriteSession(Encoding encoding, string marker, bool terminated)
        {
            var yaml = "WeekendInfo:\n  TrackName: test\n" + marker + "DriverInfo:\n  Drivers:\n  - UserName: Jos\u00E9\n";
            var bytes = encoding.GetBytes(terminated ? yaml + "\0" : yaml);
            view.Write(12, 1);
            view.Write(16, bytes.Length);
            view.Write(20, SessionInfo);
            WriteBytes(SessionInfo, bytes);
        }

        void WriteBytes(int offset, byte[] bytes)
        {
            view.WriteArray(offset, bytes, 0, bytes.Length);
        }
    }
}
