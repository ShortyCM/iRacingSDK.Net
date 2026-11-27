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

namespace iRacingSDK
{
    public static class iRSDKHeaderExtensions
    {
        public static bool HasChangedSinceReading(this iRSDKHeader header, VarBufWithIndex buf)
        {
            if (buf.index < 0 || buf.index >= BufferCount(header))
                return true;

            var current = header.varBuf[buf.index];
            return current.tickCount != buf.tickCount || current.bufOffset != buf.bufOffset
                || (UsesWriteMarkers(header) && current.TickCountBegin != buf.tickCount);
        }

        public static VarBufWithIndex FindLatestBuf(this iRSDKHeader header, int requestedTickCount)
        {
            var count = BufferCount(header);
            int maxIndex = -1;

            for (var i = 0; i < count; i++)
            {
                var b = header.varBuf[i];

                if (b.tickCount == requestedTickCount)
                    return AtIndex(header, i);

                if (maxIndex < 0 || b.tickCount > header.varBuf[maxIndex].tickCount)
                    maxIndex = i;
            }

            var current = header.CurrentBufferIndex;
            if (current < count && UsesWriteMarkers(header)
                && header.varBuf[current].tickCount == header.CurrentBufferTickCount)
                return AtIndex(header, current);

            return maxIndex == -1
                ? new VarBufWithIndex { index = -1 }
                : AtIndex(header, maxIndex);
        }

        static int BufferCount(iRSDKHeader header)
        {
            return Math.Min(header.numBuf, header.varBuf == null ? 0 : header.varBuf.Length);
        }

        static bool UsesWriteMarkers(iRSDKHeader header)
        {
            // Legacy producers leave these slots zeroed. The 1.20 sample writer
            // populates them even with header version 1.
            if (header.CurrentBufferTickCount != 0 || header.CurrentBufferIndex != 0)
                return true;

            for (var i = 0; i < BufferCount(header); i++)
                if (header.varBuf[i].TickCountBegin != 0)
                    return true;

            return false;
        }

        static VarBufWithIndex AtIndex(iRSDKHeader header, int index)
        {
            var buffer = header.varBuf[index];
            return new VarBufWithIndex { index = index, tickCount = buffer.tickCount, bufOffset = buffer.bufOffset };
        }
    }
}
