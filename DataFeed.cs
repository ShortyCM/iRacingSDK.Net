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
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace iRacingSDK
{
    public class DataFeed
    {
        const int SessionInfoUpdateOffset = 12;
        static readonly int HeaderSize = Marshal.SizeOf(typeof(iRSDKHeader));
        static readonly int VarHeaderSize = Marshal.SizeOf(typeof(VarHeader));
        static readonly int[] VarTypeSizes = { 1, 1, 4, 4, 4, 8 };
        static readonly Encoding LegacyEncoding = Encoding.GetEncoding(28591);
        static readonly Encoding Utf8Encoding = new UTF8Encoding(false, true);

        readonly MemoryMappedViewAccessor accessor;
        byte[] row;
        int? sessionInfoUpdate;
        Task<SessionData> pendingSessionInfo;
        SessionData lastSessionInfo;

        public DataFeed(MemoryMappedViewAccessor accessor)
        {
            this.accessor = accessor;
        }

        public unsafe DataSample GetNextDataSample(int requestedTickCount, bool logging)
        {
            if (accessor.Capacity < HeaderSize)
                return DisconnectedSample();

            var header = ReadHeader();
            if ((header.status & 1) == 0)
                return DisconnectedSample();

            if (header.numVars < 0 || !ContainsRange(header.varHeaderOffset, (long)header.numVars * VarHeaderSize))
                return null;

            var varHeaders = accessor.AcquirePointer(ptr => ReadVariableHeaders(header, ptr + accessor.PointerOffset));
            VarBufWithIndex varBuffer;
            if (!TryCopyRow(header, requestedTickCount, logging, out varBuffer))
                return null;

            var variables = ReadAllValues(row, varHeaders);
            var sessionData = ReadSessionInfo(header);
            if (sessionData == null)
                return DataSample.YetToConnected;

            var descriptions = varHeaders.ToDictionary(variable => variable.name, variable => variable.desc);
            variables.Add("TickCount", varBuffer.tickCount);
            variables.TickRate = header.tickRate;
            variables.TimeSeriesVariables = Array.AsReadOnly(varHeaders
                .Where(variable => variable.countAsTime)
                .Select(variable => variable.name).ToArray());
            variables.Descriptions = descriptions;
            variables.SessionData = sessionData;

            return new DataSample { Telemetry = variables, SessionData = sessionData, TelemetryDescription = descriptions, IsConnected = true };
        }

        unsafe iRSDKHeader ReadHeader()
        {
            return accessor.AcquirePointer(ptr =>
                (iRSDKHeader)Marshal.PtrToStructure(new IntPtr(ptr + accessor.PointerOffset), typeof(iRSDKHeader)));
        }

        static unsafe VarHeader[] ReadVariableHeaders(iRSDKHeader header, byte* ptr)
        {
            var varHeaders = new VarHeader[header.numVars];
            ptr += header.varHeaderOffset;

            for (var i = 0; i < varHeaders.Length; i++, ptr += VarHeaderSize)
                varHeaders[i] = (VarHeader)Marshal.PtrToStructure(new IntPtr(ptr), typeof(VarHeader));

            return varHeaders;
        }

        DataSample DisconnectedSample()
        {
            lastSessionInfo = null;
            pendingSessionInfo = null;
            sessionInfoUpdate = null;
            return DataSample.YetToConnected;
        }

        SessionData ReadSessionInfo(iRSDKHeader header)
        {
            if (header.sessionInfoUpdate != sessionInfoUpdate && header.sessionInfoLen > 0
                && ContainsRange(header.sessionInfoOffset, header.sessionInfoLen))
            {
                var sessionBytes = new byte[header.sessionInfoLen];
                accessor.ReadArray(header.sessionInfoOffset, sessionBytes, 0, sessionBytes.Length);
                Thread.MemoryBarrier();

                if (accessor.ReadInt32(SessionInfoUpdateOffset) == header.sessionInfoUpdate)
                {
                    var update = header.sessionInfoUpdate;
                    sessionInfoUpdate = update;
                    // Decode a private copy so an older task cannot read a closed mapping or publish stale data.
                    pendingSessionInfo = Task.Run(() => DecodeSessionInfo(sessionBytes, update));
                }
            }

            if (pendingSessionInfo != null && (pendingSessionInfo.IsCompleted || lastSessionInfo == null))
            {
                var decoded = pendingSessionInfo.GetAwaiter().GetResult();
                pendingSessionInfo = null;
                if (decoded == null)
                    sessionInfoUpdate = null;
                else
                    lastSessionInfo = decoded;
            }

            return lastSessionInfo;
        }

        static SessionData DecodeSessionInfo(byte[] bytes, int update)
        {
            var length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
                length = bytes.Length;
            if (length == 0)
                return null;

            try
            {
                var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
                try
                {
                    var utf8 = Utf8Encoding.GetString(bytes, 0, length).TrimStart('\uFEFF');
                    var session = DeserializeSessionInfo(deserializer, utf8, update);
                    if (utf8.Length == length || (session != null && session.WeekendInfo != null
                        && session.WeekendInfo.Encoding == "UTF8"))
                        return session;
                }
                catch (DecoderFallbackException)
                {
                    // A legacy ISO-8859-1 string need not be valid UTF-8.
                }

                return DeserializeSessionInfo(deserializer, LegacyEncoding.GetString(bytes, 0, length), update);
            }
            catch (Exception e)
            {
                Trace.WriteLine(string.Format("Error decoding session yml data {0}", e.Message), "DEBUG");
                return null;
            }
        }

        static SessionData DeserializeSessionInfo(IDeserializer deserializer, string yaml, int update)
        {
            yaml = yaml.Replace(": *", ": ");
            var result = deserializer.Deserialize<SessionData>(new StringReader(yaml));
            if (result != null)
            {
                result.Raw = yaml.Replace("\n", "\r\n");
                result.InfoUpdate = update;
            }
            return result;
        }

        bool TryCopyRow(iRSDKHeader header, int requestedTickCount, bool logging, out VarBufWithIndex buffer)
        {
            buffer = header.FindLatestBuf(requestedTickCount);
            if (buffer.index == -1 || buffer.tickCount == -1 || header.bufLen <= 0
                || !ContainsRange(buffer.bufOffset, header.bufLen))
                return false;

            if (row == null || row.Length != header.bufLen)
                row = new byte[header.bufLen];

            Thread.MemoryBarrier();
            accessor.ReadArray(buffer.bufOffset, row, 0, row.Length);
            Thread.MemoryBarrier();

            var after = ReadHeader();
            if ((after.status & 1) == 0 || after.bufLen != header.bufLen || after.numVars != header.numVars
                || after.varHeaderOffset != header.varHeaderOffset || after.HasChangedSinceReading(buffer))
            {
                Trace.WriteLineIf(logging, "iRacing changed the telemetry buffer during the read.", "DEBUG");
                return false;
            }

            return true;
        }

        bool ContainsRange(int offset, long length)
        {
            return offset >= 0 && length <= accessor.Capacity - offset;
        }

        static Telemetry ReadAllValues(byte[] bytes, VarHeader[] varHeaders)
        {
            var result = new Telemetry();
            foreach (var variable in varHeaders)
            {
                var type = (int)variable.type;
                if (type < 0 || type >= VarTypeSizes.Length || variable.count <= 0 || variable.offset < 0
                    || (long)variable.count * VarTypeSizes[type] > bytes.Length - variable.offset)
                    continue;

                result.Add(variable.name, ReadValue(bytes, variable, VarTypeSizes[type]));
            }
            return result;
        }

        static object ReadValue(byte[] bytes, VarHeader variable, int elementSize)
        {
            var offset = variable.offset;
            var count = variable.count;
            switch (variable.type)
            {
                case VarType.irChar:
                    var terminator = Array.IndexOf(bytes, (byte)0, offset, count);
                    return LegacyEncoding.GetString(bytes, offset, terminator < 0 ? count : terminator - offset);
                case VarType.irBool:
                    return count == 1 ? (object)(bytes[offset] != 0) : ReadArray<bool>(bytes, offset, count, elementSize);
                case VarType.irInt:
                case VarType.irBitField:
                    return count == 1 ? (object)BitConverter.ToInt32(bytes, offset) : ReadArray<int>(bytes, offset, count, elementSize);
                case VarType.irFloat:
                    return count == 1 ? (object)BitConverter.ToSingle(bytes, offset) : ReadArray<float>(bytes, offset, count, elementSize);
                default:
                    return count == 1 ? (object)BitConverter.ToDouble(bytes, offset) : ReadArray<double>(bytes, offset, count, elementSize);
            }
        }

        static T[] ReadArray<T>(byte[] bytes, int offset, int count, int elementSize)
        {
            var result = new T[count];
            Buffer.BlockCopy(bytes, offset, result, 0, count * elementSize);
            return result;
        }
    }
}
