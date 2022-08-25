using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;

using ASAM.XIL.Interfaces.Testbench;
using ASAM.XIL.Interfaces.Testbench.Common.CaptureResult;
using ASAM.XIL.Interfaces.Testbench.Common.Capturing;
using ASAM.XIL.Interfaces.Testbench.Common.Capturing.Enum;
using ASAM.XIL.Interfaces.Testbench.Common.Duration;
using ASAM.XIL.Interfaces.Testbench.Common.MetaInfo;
using ASAM.XIL.Interfaces.Testbench.Common.ValueContainer;
using ASAM.XIL.Interfaces.Testbench.Common.WatcherHandling;
using ASAM.XIL.Interfaces.Testbench.MAPort;
using ASAM.XIL.Interfaces.Testbench.Common.ValueContainer.Enum;
using ASAM.XIL.Interfaces.Testbench.MAPort.Enum;

// requires that System.Text.Json nuget package is installed for project
using System.Text.Json;

namespace Plexim.dNetTools.PlxAsamXilTool
{
    class VeristandXil : IPlxAsamXil
    {
        public string PortConfigFile { get; set; } = @"PortConfig.xml";
        public bool NonIntrusive { get; set; } = false;
        public string SimulationModel { get; set; } = @"[\w|\s]*";
        public string VeriStandProductVersion { get; set; } = @"2019.0.0";
        public string VeriStandProjectFile { get; set; } = "";

        public bool KeepAlive { get; set; } = false;

        private readonly EventLoop _eventLoop;
        private readonly ScopeServerDllWrapper _extModeServer;

        private bool _isConnected;
        private MAPortState _currentMAPortState;
        private ITestbench _testBench;
        private IMAPort _MAPort;
        private List<string> _signalList;
        private List<string> _parameterList;
        private string _checksumParameterName;
        private ICaptureResultWriter _captureResultWriter;
        private ICapture _capture;
        private double _baseSampleTime;
        private int _numFlattenedSignals;
        private int _numFlattenedParameters;

        private CaptureConfig _activeCaptureConfig;

        public class CaptureConfig
        {
            public enum CaptureResult
            {
                eDONE = 0,
                eERROR
            }

            public delegate void CaptureCallback(CaptureResult aResult, int[] aSignals, double[] aValues);

            public int NumPendingRequests { get; set; }

            public CaptureCallback Callback { get; set; }
            public int[] Signals { get; set; }
            public int NumSamplesRequested { get; set; }
            public ulong DecimationPeriod { get; set; } = 1;
            public int TriggerSignal { get; set; } = -1;
            public double TriggerThreshold { get; set; } = 0;
            public int TriggerEdge { get; set; } = 0;
            public int TriggerDelaySamples { get; set; } = 0;

            public bool SettingsAreEqual(CaptureConfig aConfig)
            {
                return (
                    (aConfig.Signals.SequenceEqual(Signals)) &&
                    (aConfig.NumSamplesRequested == NumSamplesRequested) &&
                    (aConfig.DecimationPeriod == DecimationPeriod) &&
                    (aConfig.TriggerSignal == TriggerSignal) &&
                    (aConfig.TriggerThreshold == TriggerThreshold) &&
                    (aConfig.TriggerEdge == TriggerEdge) &&
                    (aConfig.TriggerDelaySamples == TriggerDelaySamples)
                );
            }
        }

        async void KeepAliveTask()
        {
            CancellationTokenSource cts = _eventLoop.BeginAsyncAction();
            try
            {
                await Task.Delay(-1, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("VeriStandXil keepalive cancelled.");
            }
            catch (Exception)
            {
                Console.WriteLine("VeriStandXil keepalive exception.");
            }
            _extModeServer.Stop();
            if(_MAPort != null)
            {
                _MAPort.Disconnect();
                _capture.Dispose();

            }
            _isConnected = false;
            _eventLoop.EndAsyncAction();
        }

        public VeristandXil()
        {
            _eventLoop = new EventLoop();
            _isConnected = false;
            _extModeServer = new ScopeServerDllWrapper();
        }

        ~VeristandXil()
        {
            Cancel();
        }

        public bool Run()
        {
            _eventLoop.Post(() => KeepAliveTask());
            _eventLoop.Run();
            lock(this)
            {
                _eventLoop.Post(() => DoConnect());
                Monitor.Wait(this);
                return _isConnected;
            }
        }

        public void Cancel()
        {
            _extModeServer.Stop();
            _eventLoop.Cancel();
        }

        public bool StartSimulation()
        {
            lock(this)
            {
                _eventLoop.Post(() => DoStartSimulation());
                Monitor.Wait(this);
                return (_currentMAPortState == MAPortState.eSIMULATION_RUNNING);
            }
        }

        public bool StopSimulation()
        {
            lock (this)
            {
                _eventLoop.Post(() => DoStopSimulation());
                Monitor.Wait(this);
                return (_currentMAPortState == MAPortState.eSIMULATION_STOPPED);
            }
        }

        public bool StartExtModeServer()
        {
            return _extModeServer.Start((string aRequest) => _eventLoop.Post(() => DoExtModeCallback(aRequest)));
        }

        public void Join()
        {
            _eventLoop.Join();
        }

        public void PreConnect(string aVeriStandProjectFile)
        {
            // TODO: Open project to avoid error:  The gateway at the specified IP address is running a different system definition file than the file you specified.
            // Happens during the run stage
            VeristandXilHelper.OpenProject(aVeriStandProjectFile);
        }

        private void DoExtModeCallback(string aRequest)
        {
            _eventLoop.AssertRunningInEventThread();
            if(!_isConnected)
            {
                return;
            }
            ScopeServerDllWrapper.Request req = JsonSerializer.Deserialize<ScopeServerDllWrapper.Request>(aRequest);
            switch (req.Command)
            {
                case -1:
                    {
                        if (_activeCaptureConfig != null)
                        {
                            // abort any pending scope requests
                            _activeCaptureConfig.NumPendingRequests = 0;
                            _activeCaptureConfig = null;
                        }
                        if (!KeepAlive)
                        {
                            _eventLoop.Cancel();
                        }
                    }
                    break;
                case 0:
                    {
                        string checksum = "";
                        if (_checksumParameterName != "")
                        {
                            Console.WriteLine("Reading checksum from: " + _checksumParameterName);
                            IBaseValue val = _MAPort.Read(_checksumParameterName);
                            if ((val != null)  && (val.Type == DataType.eFLOAT_VECTOR))
                            {
                                IFloatVectorValue vv = (IFloatVectorValue)val;
                                for (int j = 0; j < vv.Count; j++)
                                {
                                    checksum += ((ulong)vv.GetValueByIndex(j)).ToString("x8");
                                }
                            }
                            Console.WriteLine("Retrieved: " + checksum);
                        }

                        ScopeServerDllWrapper.ModelInfoReply reply = new ScopeServerDllWrapper.ModelInfoReply();
                        reply.BaseTimeStep = _baseSampleTime;
                        reply.Checksum = checksum;
                        reply.NumSignals = _numFlattenedSignals;
                        reply.NumParameters = _numFlattenedParameters;
                        _extModeServer.Send(JsonSerializer.Serialize(reply));
                    }
                    break;

                case 1:
                    {
                        ScopeServerDllWrapper.ScopeRequest request = JsonSerializer.Deserialize<ScopeServerDllWrapper.ScopeRequest>(aRequest);
                        CaptureConfig config = new CaptureConfig();
                        config.NumSamplesRequested = request.NumSamples;
                        config.Signals = request.Signals;
                        config.NumPendingRequests = 1;
                        config.DecimationPeriod = (ulong)request.DecimationPeriod;
                        config.TriggerSignal = request.TriggerChannel;
                        config.TriggerThreshold = request.TriggerValue;
                        config.TriggerEdge = request.TriggerEdge;
                        config.TriggerDelaySamples = request.TriggerDelay;
                        config.Callback = (CaptureConfig.CaptureResult aResult, int[] aSignals, double[] aValues) =>
                        {
                             ScopeServerDllWrapper.ScopeReply reply = new ScopeServerDllWrapper.ScopeReply();
                             reply.ErrorCode = 0;
                             reply.TransactionId = request.TransactionId;
                             reply.NumSamples = aValues.Length/aSignals.Length;
                             reply.SampleTime = _baseSampleTime * request.DecimationPeriod;
                             reply.Signals = aSignals;
                             reply.Samples = aValues;
                            _extModeServer.Send(JsonSerializer.Serialize(reply));
                        };

                        if (_activeCaptureConfig == null)
                        {
                            _activeCaptureConfig = config;
                            _eventLoop.Post(() => DoArmScope(config));
                        }
                        else
                        {
                            if (_activeCaptureConfig.SettingsAreEqual(config))
                            {
                                //Console.WriteLine("Request with equal scope settings");
                                if (_activeCaptureConfig.NumPendingRequests > 0)
                                {
                                    //Console.WriteLine("Request pending");
                                    _activeCaptureConfig.NumPendingRequests = 2; // don't queue up more than one additional trigger
                                }
                                else
                                {
                                    _eventLoop.Post(() => DoArmScope(config));
                                }
                            }
                            else
                            {
                               // Console.WriteLine("New scope request");
                                _activeCaptureConfig.NumPendingRequests = 0; // stop pending request
                                _activeCaptureConfig = config;
                                _eventLoop.Post(() => DoArmScope(config));
                            }
                        }
                    }
                    break;

                case 2:
                    {
                        ScopeServerDllWrapper.TuneParamsRequest request = JsonSerializer.Deserialize<ScopeServerDllWrapper.TuneParamsRequest>(aRequest);
                        int index = 0;
                        foreach (string parameter in _parameterList)
                        {
                            ulong sizeX = _MAPort.GetVariableInfo(parameter).XSize;
                            ulong sizeY = _MAPort.GetVariableInfo(parameter).YSize;
                            bool isDirty = false;
                            IBaseValue val = _MAPort.Read(parameter);

                            if (val.Type == DataType.eFLOAT)
                            {
                                if (((IFloatValue)val).Value != request.Values[index])
                                {
                                    ((IFloatValue)val).Value = request.Values[index];
                                    isDirty = true;
                                }
                                index++;
                            }
                            else if (val.Type == DataType.eFLOAT_VECTOR)
                            {
                                IFloatVectorValue vv = (IFloatVectorValue)val;
                                for (int j = 0; j < vv.Count; j++)
                                {
                                    if (vv.GetValueByIndex(j) != request.Values[index])
                                    {
                                        vv.SetValueByIndex(j, request.Values[index]);
                                        isDirty = true;
                                    }
                                    index++;
                                }
                            }
                            if(isDirty)
                            {
                                _MAPort.Write(parameter, val);
                            }
                        }
                        //Not implemented: _MAPort.WriteSimultaneously(_parameterList, parameterValues,"");
                    }
                    break;

                default:
                    {
                        ScopeServerDllWrapper.ErrorReply reply = new ScopeServerDllWrapper.ErrorReply();
                        reply.ErrorMessage = "Unsupported Command.";
                        _extModeServer.Send(JsonSerializer.Serialize(reply));
                    }
                    break;
            }
        }

        private void DoConnect()
        {
            _testBench = VeristandXilHelper.GetTestbench(VeriStandProductVersion);
            if (_testBench == null)
            {
                Console.WriteLine("Unable to obtain test bench.");
                lock (this)
                {
                    Monitor.PulseAll(this);
                }
                return;
            }
            _MAPort = VeristandXilHelper.GetMAPort(_testBench, PortConfigFile, !NonIntrusive);
            if (_MAPort == null)
            {
                Console.WriteLine("Unable to load model.");
                lock (this)
                {
                    Monitor.PulseAll(this);
                }
                return;
            }

            // Console.WriteLine("Model loaded: " + _MAPort.Name);
            // _MAPort.SimulationStepSize not implemented!
            // _MAPort.Configuration.ModelFile niviproj

            // populate signals and parameters list
            IList<string> VariableNames = _MAPort.VariableNames;
            Console.WriteLine(string.Format("-----Signals-----"));
            _signalList = VeristandXilHelper.GetChannelSubset(VariableNames, new Regex(@"Targets/[\w|\s]*/Simulation Models/Models/" + SimulationModel + @"/Signals/[\w|\s]*"));
            _numFlattenedSignals = 0;
            foreach (string signals in _signalList)
            {
                ulong sizeX = _MAPort.GetVariableInfo(signals).XSize;
                ulong sizeY = _MAPort.GetVariableInfo(signals).YSize;
                if ((sizeX != 0) || (sizeY != 0))
                {
                    Console.WriteLine("Unsupported signal X/Y size.");
                    lock (this)
                    {
                        Monitor.PulseAll(this);
                    }
                    return;
                }
                _numFlattenedSignals += 1;
            }

            Console.WriteLine(string.Format("-----Parameters-----"));
            List<string> allParameters = VeristandXilHelper.GetChannelSubset(VariableNames, new Regex(@"Targets/[\w|\s]*/Simulation Models/Models/" + SimulationModel + @"/Parameters/[\w|\s]*"));
            List<int> _parameterOrder = new List<int>();
            List<string> _parameterWorkingList = new List<string>();
            _parameterList = new List<string>();

            int parameterIndex = 0;
            Regex parameterIndexRegex = new Regex(@".+_\((?<paramID>\d+)\)$");

            _checksumParameterName = "";
            foreach (string parameter in allParameters)
            {
                if(parameter.EndsWith("_checksum"))
                {
                    _checksumParameterName = parameter;
                }
                else
                {
                    _parameterWorkingList.Add(parameter);
                    Match paramMatch = parameterIndexRegex.Match(parameter);
                    parameterIndex = int.Parse(paramMatch.Groups["paramID"].Value);
                    _parameterOrder.Add(parameterIndex);
                }
            }

            //Reorder parameters according to index.  E.g. _parameterList[N]=parameter_name_(N).
            List<int> _parameterOrderIdx = Enumerable.Range(0, _parameterOrder.Count).ToList<int>();
            _parameterOrderIdx.Sort((a, b) => _parameterOrder[a].CompareTo(_parameterOrder[b]));
            foreach (int i in _parameterOrderIdx.AsEnumerable())
            {
                _parameterList.Add(_parameterWorkingList[i]);
            }

            _numFlattenedParameters = 0;
            foreach (string parameter in _parameterList)
            {
                ulong sizeX = _MAPort.GetVariableInfo(parameter).XSize;
                ulong sizeY = _MAPort.GetVariableInfo(parameter).YSize;
                if(sizeY != 0)
                {
                    Console.WriteLine("Unsupported parameter Y size.");
                    lock (this)
                    {
                        Monitor.PulseAll(this);
                    }
                    return;
                }

                if (sizeX == 0) //Scalar
                {
                    _numFlattenedParameters += (int)1;
                }
                else            //Vector
                {
                    _numFlattenedParameters += (int)sizeX;
                }
            }

            IList<ITaskInfo> taskInfos = _MAPort.TaskInfos;
            if(taskInfos.Count == 0)
            {
                Console.WriteLine("No task available.");
                lock (this)
                {
                    Monitor.PulseAll(this);
                }
                return;
            }

            Console.WriteLine(string.Format("Number of tasks: {0}", taskInfos.Count));
            _baseSampleTime = taskInfos[0].Period;
            Console.WriteLine(string.Format("Base sample time: {0}", _baseSampleTime));

            // prepare signal capturing
            _captureResultWriter = _testBench.CapturingFactory.CreateCaptureResultMemoryWriter();
            _capture = _MAPort.CreateCapture(taskInfos[0].Name); // create a capture task based on base task

            lock (this)
            {
                _isConnected = true;
                Monitor.PulseAll(this);
            }
        }

        private void DoStartSimulation()
        {
            // eDISCONNECTED
            // eSIMULATION_RUNNING
            // eSIMULATION_PAUSED
            // eSIMULATION_STOPPED
            if (_MAPort.State != MAPortState.eSIMULATION_RUNNING)
            {
                _MAPort.StartSimulation();
            }
            lock (this)
            {
                _currentMAPortState = _MAPort.State;
                Monitor.PulseAll(this);
            }
        }

        private void DoStopSimulation()
        {
            if (_MAPort.State != MAPortState.eSIMULATION_STOPPED)
            {
                _MAPort.StopSimulation();
            }
            lock (this)
            {
                _currentMAPortState = _MAPort.State;
                Monitor.PulseAll(this);
            }
        }

        private void DoArmScope(CaptureConfig aConfig)
        {
            if (!_isConnected)
            {
                return;
            }
            if (aConfig.Signals.Length == 0)
            {
                return;
            }
            if (aConfig.NumPendingRequests == 0)
            {
                Console.WriteLine("Arming cancelled");
                return;
            }

            //Console.WriteLine("Arming scope");

            /*
            eCONFIGURED = 0,
            eACTIVATED = 1,
            eRUNNING = 2,
            eFINISHED = 3
            */
            try
            {
                if (_capture.State != CaptureState.eCONFIGURED)
                {
                    _capture.Stop();
                }

                List<string> signals = new List<string>();
                for (int i = 0; i < aConfig.Signals.Length; i++)
                {
                    signals.Add(_signalList[i]);
                }

                // Configure the captue task properties.
                _capture.ClearConfiguration();
                _capture.Variables = signals;
                _capture.Downsampling = aConfig.DecimationPeriod;
                _capture.DurationUnit = DurationUnit.eSAMPLES; //DurationUnit.eSECONDS; 
                                                               //capture.MinBufferSize = -1; // By default entire buffer reserved.
                _capture.Retriggering = 0; //-1 is continuous, 0=once, > 0 Ntimes retrigger.

                // Configure start and stop triggers.
                if (aConfig.TriggerSignal >= 0)
                {
                    string startTriggerCondition;
                    if (aConfig.TriggerEdge == 0)
                    {
                        startTriggerCondition = string.Format("posedge(Trigger, {0})", aConfig.TriggerThreshold);
                    }
                    else
                    {
                        startTriggerCondition = string.Format("negedge(Trigger, {0})", aConfig.TriggerThreshold);
                    }

                    var startTriggerDefines = new Dictionary<string, string>() { { "Trigger", _signalList[aConfig.TriggerSignal] } };
                    IConditionWatcher startTrigger = _testBench.WatcherFactory.CreateConditionWatcher(startTriggerCondition, startTriggerDefines);
                    double TriggerDelaySamples = 0; // TODO: this does not work aConfig.TriggerDelaySamples * (int)aConfig.DecimationPeriod;
                    _capture.SetStartTriggerCondition(startTrigger, TriggerDelaySamples);
                    //TODO: Supposedly SetStartTriggerCondition() depreciated, but cannot create an IDuration without crashing.
                    //IDuration startTriggerDelay = _testBench.DurationFactory.CreateCycleNumberDuration(aConfig.TriggerDelaySamples);
                    //_capture.SetStartTrigger(startTrigger, startTriggerDelay);
                }

                // configure exit conditions (after captured N Samples).
                IDurationWatcher stopTrigger = _testBench.WatcherFactory.CreateDurationWatcher(aConfig.NumSamplesRequested * (int)aConfig.DecimationPeriod); //samples

                _capture.SetStopTriggerCondition(stopTrigger);
                _capture.Start(_captureResultWriter);
                _eventLoop.Post(() => DoWaitForScopeData(aConfig));
            }
            catch (Exception ex)
            {
                aConfig.NumPendingRequests = 0;
                _eventLoop.Cancel();
                Console.Error.WriteLine("Execption in DoArmScope: "+ex.Message);
            }
        }

        private void DoWaitForScopeData(CaptureConfig aConfig)
        {
            if (!_isConnected)
            {
                return;
            }
            if (aConfig.NumPendingRequests == 0)
            {
                return;
            }

            if ((_capture.State == CaptureState.eACTIVATED) || (_capture.State == CaptureState.eRUNNING))
            {
                _eventLoop.Post(() => DoWaitForScopeData(aConfig));
                return;
            }
            else if (_capture.State != CaptureState.eFINISHED)
            {
                aConfig.Callback(CaptureConfig.CaptureResult.eERROR, aConfig.Signals, new double[0]);
                aConfig.NumPendingRequests = 0;
                return;
            }

            ICaptureResult captureResult = null;
            try
            {
                captureResult = _capture.Fetch(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Fetch exception occurred!");
                aConfig.Callback(CaptureConfig.CaptureResult.eERROR, aConfig.Signals, new double[0]);
                aConfig.NumPendingRequests = 0;
                _eventLoop.Cancel();
                return;
            }

            if(captureResult.SignalGroupNames.Count != 1)
            {
                Console.WriteLine("Invalid number of SignalGroupNames!");
                aConfig.Callback(CaptureConfig.CaptureResult.eERROR, aConfig.Signals, new double[0]);
                aConfig.NumPendingRequests = 0;
                _eventLoop.Cancel();
                return;
            }

            ISignalGroupValue signalGroupValue = captureResult.GetSignalGroupValue(captureResult.SignalGroupNames[0]);

            if (signalGroupValue.YVectors.Count != aConfig.Signals.Length)
            {
                Console.WriteLine("Invalid number of signals!");
                aConfig.Callback(CaptureConfig.CaptureResult.eERROR, aConfig.Signals, new double[0]);
                aConfig.NumPendingRequests = 0;
                _eventLoop.Cancel();
                return;
            }

            long numSamples = signalGroupValue.XVector.Count;
            if(numSamples <= 0)
            {
                Console.WriteLine("Invalid number of samples!");
                aConfig.Callback(CaptureConfig.CaptureResult.eERROR, aConfig.Signals, new double[0]);
                aConfig.NumPendingRequests = 0;
                _eventLoop.Cancel();
                return;
            }

            if(numSamples > aConfig.NumSamplesRequested)
            {
                numSamples = aConfig.NumSamplesRequested;
            }

            double[] values = new double[numSamples * aConfig.Signals.Length];

            /*
             * NOTE: Spacing is unequal! 
             * We may need to interpolate data or implent new functionality in PLECS.
             */
            /*
            double minDelta = Double.MaxValue;
            double maxDelta = 0;
            double lastVal = 0 ;
            bool firstVal = true;
            foreach (double val in ((IFloatVectorValue)signalGroupValue.XVector).Value)
            {
                Console.WriteLine(string.Format("X Timestamp {0,10}", val));
                if (firstVal)
                {
                    firstVal = false;
                    lastVal = val;
                }
                else
                {
                    double delta = val - lastVal;
                    lastVal = val;
                    if (maxDelta < delta)
                    {
                        maxDelta = delta;
                    }    
                    if(minDelta > delta)
                    {
                        minDelta = delta;
                    }
                }
            }
            Console.WriteLine(string.Format("Max Delta {0,10}", maxDelta));
            Console.WriteLine(string.Format("Min Delta {0,10}", minDelta));
            */

            for (int y=0; y< signalGroupValue.YVectors.Count; y++)
            {
                // iterate through signals
                IVectorValue vectorValue = signalGroupValue.YVectors[y];
                for (int x=0; (x< vectorValue.Count) && (x < numSamples); x++)
                {
                    // iterate through samples
                    values[x* signalGroupValue.YVectors.Count + y] = ((IFloatVectorValue)vectorValue).Value[x];
                }
            }

            aConfig.Callback(CaptureConfig.CaptureResult.eDONE, aConfig.Signals, values);
            aConfig.NumPendingRequests--;
            if(aConfig.NumPendingRequests > 0)
            {
                _eventLoop.Post(() => DoArmScope(aConfig));
            }
        }
    }
}
