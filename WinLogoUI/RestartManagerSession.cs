﻿///
/// This code is copied from the gist at https://gist.github.com/falahati/34b23831733151460de1368c5fba8e93
///
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RestartManager
{
    /// <summary>
    ///     Represents a Restart Manager session. The Restart Manager enables all but the critical system services to be shut
    ///     down and restarted with aim of eliminate or reduce the number of system restarts that are required to complete an
    ///     installation or update.
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    public class RestartManagerSession : IDisposable
    {
        public delegate void WriteStatusCallback(uint percentageCompleted);

        [Flags]
        public enum ShutdownType : uint
        {
            /// <summary>
            ///     Default behavior
            /// </summary>
            Normal = 0,

            /// <summary>
            ///     Force unresponsive applications and services to shut down after the timeout period. An application that does not
            ///     respond to a shutdown request is forced to shut down within 30 seconds. A service that does not respond to a
            ///     shutdown request is forced to shut down after 20 seconds.
            /// </summary>
            ForceShutdown = 0x1,

            /// <summary>
            ///     Shut down applications if and only if all the applications have been registered for restart using the
            ///     RegisterApplicationRestart function. If any processes or services cannot be restarted, then no processes or
            ///     services are shut down.
            /// </summary>
            ShutdownOnlyRegistered = 0x10
        }

        public RestartManagerSession()
        {
            SessionKey = Guid.NewGuid().ToString();
            var errorCode = StartSession(out var sessionHandle, 0, SessionKey);

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }

            Handle = sessionHandle;
        }

        public RestartManagerSession(string sessionKey)
        {
            SessionKey = sessionKey;
            var errorCode = JoinSession(out var sessionHandle, SessionKey);

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }

            Handle = sessionHandle;
        }

        private IntPtr Handle { get; }
        public string SessionKey { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        [DllImport("rstrtmgr", EntryPoint = "RmEndSession")]
        private static extern int EndSession(IntPtr sessionHandle);

        [DllImport("rstrtmgr", EntryPoint = "RmJoinSession", CharSet = CharSet.Auto)]
        private static extern int JoinSession(out IntPtr sessionHandle, string strSessionKey);

        [DllImport("rstrtmgr", EntryPoint = "RmRegisterResources", CharSet = CharSet.Auto)]
        // ReSharper disable once TooManyArguments
        private static extern int RegisterResources(
            IntPtr sessionHandle,
            uint numberOfFiles,
            string[] fileNames,
            uint numberOfApplications,
            UniqueProcess[] applications,
            uint numberOfServices,
            string[] serviceNames);

        [DllImport("rstrtmgr", EntryPoint = "RmRestart")]
        private static extern int Restart(IntPtr sessionHandle, int restartFlags, WriteStatusCallback statusCallback);

        [DllImport("rstrtmgr", EntryPoint = "RmShutdown")]
        private static extern int Shutdown(
            IntPtr sessionHandle,
            ShutdownType actionFlags,
            WriteStatusCallback statusCallback);

        [DllImport("rstrtmgr", EntryPoint = "RmStartSession", CharSet = CharSet.Auto)]
        private static extern int StartSession(out IntPtr sessionHandle, int sessionFlags, string strSessionKey);

        public void RegisterProcess(params Process[] processes)
        {
            var errorCode = RegisterResources(Handle,
                0, new string[0],
                (uint)processes.Length, processes.Select(UniqueProcess.FromProcess).ToArray(),
                0, new string[0]);

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }
        }

        public void Restart(WriteStatusCallback statusCallback)
        {
            var errorCode = Restart(Handle, 0, statusCallback);

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }
        }

        public void Restart()
        {
            Restart(null);
        }

        public void Shutdown(ShutdownType shutdownType, WriteStatusCallback statusCallback)
        {
            var errorCode = Shutdown(Handle, shutdownType, statusCallback);

            if (errorCode != 0)
            {
                throw new Win32Exception(errorCode);
            }
        }

        public void Shutdown(ShutdownType shutdownType)
        {
            Shutdown(shutdownType, null);
        }

        private void ReleaseUnmanagedResources()
        {
            try
            {
                EndSession(Handle);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <inheritdoc />
        ~RestartManagerSession()
        {
            ReleaseUnmanagedResources();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            private uint LowDateTime;
            private uint HighDateTime;

            public static FileTime FromDateTime(DateTime dateTime)
            {
                var ticks = dateTime.ToFileTime();

                return new FileTime
                {
                    HighDateTime = (uint)(ticks >> 32),
                    LowDateTime = (uint)(ticks & uint.MaxValue)
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UniqueProcess
        {
            private int ProcessId;
            private FileTime ProcessStartTime;

            public static UniqueProcess FromProcess(Process process)
            {
                return new UniqueProcess
                {
                    ProcessId = process.Id,
                    ProcessStartTime = FileTime.FromDateTime(process.StartTime)
                };
            }
        }
    }
}
