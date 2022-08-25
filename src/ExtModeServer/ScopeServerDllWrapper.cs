using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Plexim.dNetTools.PlxAsamXilTool
{
    class ScopeServerDllWrapper
    {
        public delegate void Callback(string text);
      
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetDllDirectory(string lpPathName);

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMS_getDllVersion();

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMS_obtainHandle();

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void XMS_releaseHandle(int aHandle);

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void XMS_releaseAllHandles();

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int XMS_start(int aHandle, int aPort, Callback aRequestCallBack);

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        // UnmanagedType.LPStr: single-byte, NULL terminated ANSI character string (default)
        // UnmanagedType.LPUTF8Str: UTF-8 encoded string
        private static extern int XMS_send(int aHandle, [MarshalAs(UnmanagedType.LPStr)] string aRequest);

        [DllImport("PlxExtModeServer.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void XMS_stop(int aHandle);

        private static void AddEnvironmentPaths(IEnumerable<string> paths){
            var path = new[] { Environment.GetEnvironmentVariable("PATH") ?? string.Empty };

            string newPath = string.Join(Path.PathSeparator.ToString(), path.Concat(paths));

            Environment.SetEnvironmentVariable("PATH", newPath);
        }

        public class ModelInfoReply
        {
            public int Command
            {
                get { return 3; }
            }

            public double BaseTimeStep { get; set; }
            public string Checksum { get; set; }
            public int NumSignals { get; set; }
            public int NumParameters { get; set; }
        }

        public class TuneParamsRequest
        {
            public double[] Values { get; set; }
        }

        public class TuneParamsReply
        {
            public int Command
            {
                get { return 5; }
            }
            public int ErrorCode { get; set; }
        }

        public class ScopeRequest
        {
            public int TransactionId { get; set; }
            public int[] Signals { get; set; }
            public int NumSamples { get; set; }
            public int DecimationPeriod { get; set; }
            public int TriggerChannel { get; set; }
            public int TriggerEdge { get; set; }
            public double TriggerValue { get; set; }
            public int TriggerDelay { get; set; }
        }

        public class ScopeReply
        {
            public int Command
            {
                get { return 4; }
            }

            public int TransactionId { get; set; }
            public int ErrorCode { get; set; }
            public int NumSamples { get; set; }
            public double SampleTime { get; set; }
            public int[] Signals { get; set; }
            public double[] Samples { get; set; }
        }

        public class ErrorReply
        {
            public int Command
            {
                get { return 6; }
            }

            public string ErrorMessage { get; set; }
        }

        public class Request
        {
            public int Command { get; set; } = -1;
        }

        public delegate void DisconnectCallback();

        private readonly Callback _callback;
        private readonly int _handle;
        private Callback _registeredCallback;

        public ScopeServerDllWrapper()
        {
            _callback = new Callback(Handler);
            _handle = XMS_obtainHandle();
        }

        ~ScopeServerDllWrapper()
        {
            XMS_releaseAllHandles();
        }

        private void Handler(string aRequest)
        {
            _registeredCallback(aRequest);
        }

        public bool Start(Callback aRegisteredCallback)
        {
            if(_handle == 0)
            {
                return false;
            }
            _registeredCallback = aRegisteredCallback;
            return (XMS_start(_handle, 9999, _callback) == 0);
        }

        public bool Send(string mMessage)
        {
            return (XMS_send(_handle, mMessage) == 0);
        }

        public void Stop()
        {
            if(_handle != 0)
            {
                XMS_stop(_handle);
            }
        }
    }
}