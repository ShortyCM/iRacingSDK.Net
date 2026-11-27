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
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Win32.Synchronization;

namespace iRacingSDK
{
    class iRacingMemory : IDisposable
    {
        readonly string mappingName;
        readonly string eventName;
        public MemoryMappedViewAccessor Accessor { get; private set; }
        SafeWaitHandle dataValidEvent;
        MemoryMappedFile irsdkMappedMemory;

        public iRacingMemory(string mappingName = "Local\\IRSDKMemMapFileName", string eventName = "Local\\IRSDKDataValidEvent")
        {
            this.mappingName = mappingName;
            this.eventName = eventName;
        }

        public bool TryOpen()
        {
            if (Accessor != null)
                return true;

            var handle = Event.OpenEvent(Event.SYNCHRONIZE, false, eventName);
            if (handle.IsInvalid)
            {
                var lastError = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (lastError != Event.ERROR_FILE_NOT_FOUND)
                    Trace.WriteLine(string.Format("Unable to open event {0} - Error Code {1}", eventName, lastError), "DEBUG");
                return false;
            }

            MemoryMappedFile mapping = null;
            try
            {
                mapping = MemoryMappedFile.OpenExisting(mappingName, MemoryMappedFileRights.Read);
                Accessor = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                irsdkMappedMemory = mapping;
                dataValidEvent = handle;
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException e)
            {
                Trace.WriteLine(e.Message, "DEBUG");
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Trace.WriteLine(e.Message, "DEBUG");
                return false;
            }
            finally
            {
                if (Accessor == null)
                {
                    if (mapping != null)
                        mapping.Dispose();
                    handle.Dispose();
                }
            }
        }

        public void WaitForData()
        {
            Event.WaitForSingleObject(dataValidEvent, 17);
        }

        public void Dispose()
        {
            if (Accessor == null)
                return;

            Accessor.Dispose();
            irsdkMappedMemory.Dispose();
            dataValidEvent.Dispose();
            Accessor = null;
            irsdkMappedMemory = null;
            dataValidEvent = null;
        }
    }
}
