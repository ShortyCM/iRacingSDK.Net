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
using System.Threading;
using System.Diagnostics;
using iRacingSDK.Support;

namespace iRacingSDK
{
    public class iRacingConnection
    {
        readonly CrossThreadEvents connected = new CrossThreadEvents();
        readonly CrossThreadEvents disconnected = new CrossThreadEvents();
        readonly CrossThreadEvents<DataSample> newSessionData = new CrossThreadEvents<DataSample>();
        
        static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
        readonly iRacingMemory iRacingMemory;
        int isRunning;
        internal bool IsRunning { get { return Volatile.Read(ref isRunning) != 0; } }

        long processingTime;
        public long ProcessingTime { get { return processingTime * 1000000L / Stopwatch.Frequency; } }

        long waitingTime;
        public long WaitingTime { get { return waitingTime * 1000000L / Stopwatch.Frequency; } }

        long yieldTime;
        public long YieldTime { get { return (yieldTime * 1000000L / Stopwatch.Frequency) ; } }

        public readonly Replay Replay;
        public readonly PitCommand PitCommand;

        public bool IsConnected { get; private set; }

        public event Action Connected
        {
            add { connected.Event += value; }
            remove { connected.Event -= value; }
        }

        public event Action Disconnected
        {
            add { disconnected.Event += value; }
            remove { disconnected.Event -= value; }
        }

        public event Action<DataSample> NewSessionData
        {
            add { newSessionData.Event += value; }
            remove { newSessionData.Event -= value; }
        }

        public iRacingConnection()
            : this(new iRacingMemory())
        {
        }

        internal iRacingConnection(iRacingMemory memory)
        {
            this.iRacingMemory = memory;
            this.Replay = new Replay(this);
            this.PitCommand = new PitCommand();
        }

        public IEnumerable<DataSample> GetDataFeed(bool logging = true)
        {
            return GetRawDataFeed(logging).WithLastSample().WithEvents(connected, disconnected, newSessionData);
        }

        internal IEnumerable<DataSample> GetRawDataFeed(bool logging = true)
        {
            if (Interlocked.CompareExchange(ref isRunning, 1, 0) != 0)
                throw new Exception("Can not call GetDataFeed concurrently.");

            try
            {
                while (true)
                {
                    foreach (var notConnectedSample in WaitForInitialConnection())
                    {
                        IsConnected = false;
                        yield return notConnectedSample;
                    }

                    foreach (var sample in AllSamples(logging))
                    {
                        IsConnected = sample.IsConnected;
                        yield return sample;
                    }
                }
            }
            finally
            {
                iRacingMemory.Dispose();
                Volatile.Write(ref isRunning, 0);
            }
        }

        IEnumerable<DataSample> WaitForInitialConnection()
        {
            bool wasConnected = IsConnected;
            TraceInfo.WriteLineIf(!wasConnected, "Waiting to connect to iRacing application");

            while (!iRacingMemory.TryOpen())
            {
                yield return DataSample.YetToConnected;
                Thread.Sleep(10);
            }

            TraceInfo.WriteLineIf(!wasConnected, "Connected to iRacing application");
        }

        IEnumerable<DataSample> AllSamples(bool logging)
        {
            var dataFeed = new DataFeed(iRacingMemory.Accessor);
            int? lastTickCount = null;
            var nextTickCount = 0;
            var timeSinceLastTick = Stopwatch.StartNew();
            var timer = new Stopwatch();

            while (true)
            {
                timer.Restart();
                var data = dataFeed.GetNextDataSample(nextTickCount, logging);
                processingTime = timer.ElapsedTicks;

                if (data != null && data.IsConnected && data.Telemetry.TickCount != lastTickCount)
                {
                    var tickCount = data.Telemetry.TickCount;
                    if (logging && lastTickCount.HasValue && tickCount > nextTickCount)
                        TraceWarning.WriteLine("Dropped telemetry ticks from {0} to {1}.", nextTickCount, tickCount - 1);

                    lastTickCount = tickCount;
                    nextTickCount = unchecked(tickCount + 1);
                    timeSinceLastTick.Restart();
                    timer.Restart();
                    yield return data;
                    yieldTime = timer.ElapsedTicks;
                    continue;
                }

                if (timeSinceLastTick.Elapsed >= ConnectionTimeout)
                {
                    iRacingMemory.Dispose();
                    yield return DataSample.YetToConnected;
                    yield break;
                }

                if (data != null && !data.IsConnected)
                {
                    lastTickCount = null;
                    nextTickCount = 0;
                    yield return data;
                }

                timer.Restart();
                iRacingMemory.WaitForData();
                waitingTime = timer.ElapsedTicks;
            }
        }
    }
}
