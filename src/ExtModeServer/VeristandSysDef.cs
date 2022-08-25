using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using NationalInstruments.VeriStand;
using NationalInstruments.VeriStand.SystemDefinitionAPI;

// requires that System.Text.Json nuget package is installed for project
using System.Text.Json;

namespace Plexim.dNetTools.PlxAsamXilTool
{

    class VeristandSysDef
    {
        private const int Success = 0;
        private const int Failure = 1;


        public static int Generate(string BaseName, string ModelFile, string CodeGenPath)
        {
            /*  
             *  Key assumptions:
             *  1) Only one model
             *  2) Only one chassis/target
             */

            Console.WriteLine("Parsing generated *.json files...");

            //Read Registry and Hardware JSON
            string RegFile = Path.Combine(CodeGenPath, BaseName + "_reg.json");
            string HwFile = Path.Combine(CodeGenPath, BaseName + "_hw.json");

            if (!File.Exists(RegFile))
            {
                Console.WriteLine("Cannot find model registry file: " + RegFile + ". Confirm Build Type is Veristand Engine and code is generated.");
                return Failure;
            }
            else if (!File.Exists(HwFile))
            {
                Console.WriteLine("Cannot find hardware registry file: " + HwFile + ". Confirm Build Type is Veristand Engine and code is generated.");
                return Failure;
            }

            //Read Registry JSON
            string RegJsonString = File.ReadAllText(RegFile);
            SysDefWrapper.Registry Reg = JsonSerializer.Deserialize<SysDefWrapper.Registry>(RegJsonString);

            //Read Hardware JSON
            string HwJsonString = File.ReadAllText(HwFile);
            SysDefWrapper.Hardware Hw = JsonSerializer.Deserialize<SysDefWrapper.Hardware>(HwJsonString);

            //Open the Temporary System Definition File (*.nivssdf.tmp)
            string SysDefFile = Path.Combine(CodeGenPath, BaseName + ".nivssdf.tmp");

            Console.WriteLine("Opening system definition file...");
            if (!File.Exists(SysDefFile))
            {
                Console.WriteLine("Cannot find system definition file: " + SysDefFile);
                return Failure;
            }

            SystemDefinition _SystemDefinition = new SystemDefinition(SysDefFile);

            //Use the first target in the System Defintion File.
            Target _Target = _SystemDefinition.Root.GetTargets().GetTargetList()[0];

            // Target.TargetRate, DAQDigitalLinesRate, and OperatingSystem configured by CodeGen project.
            //ConfigureTarget(_Target, 1000.0, "192.168.0.10");

            List<string> ModelInputPaths;
            List<string> ModelOutputPaths;
            List<string> HardwareInputPaths;
            List<string> HardwareOutputPaths;

            ConfigureTarget(_Target, Hw);

            AddModel(_Target, ModelFile, BaseName, out ModelInputPaths, out ModelOutputPaths);
          
            AddHardware(_Target, _SystemDefinition, Reg, Hw, out HardwareInputPaths, out HardwareOutputPaths);
            
            MapModelToHardware( _SystemDefinition, ModelInputPaths, ModelOutputPaths, HardwareInputPaths, HardwareOutputPaths);

            //Save changes to the System Definition File (*.nivssdf)
            string ErrorString;
            string SysDefOutFile = Path.Combine(CodeGenPath, BaseName + ".nivssdf");
            _SystemDefinition.SaveSystemDefinitionFile(SysDefOutFile, out ErrorString);
            if (ErrorString == "")
            {
                Console.WriteLine("System Definition File saved successfully...");
            }
            else
            {
                Console.WriteLine("There was an error saving the System Definition...\n" + ErrorString);
            }

            return Success;
        }
        private static int ConfigureTarget(Target _Target, SysDefWrapper.Hardware Hw)
        {
            _Target.OperatingSystem = "Linux_x64";
            _Target.TargetRate = Hw.targRate;
            _Target.DAQDigitalLinesRate = Hw.targRate;
            _Target.Username = Hw.targUserName;
            _Target.Password = Hw.targPassword;

            string TargetIPAddress = Hw.targIP;
            if (IPAddress.TryParse(TargetIPAddress, out IPAddress IP))
            {
                _Target.IPAddress = TargetIPAddress;
            }
            else
            {
                Console.WriteLine("Specified IP address " + TargetIPAddress + " has an invalid format");
                return Failure;
            }

            return Success;
        }

        private static int AddModel(Target _Target, string ModelFile, string BaseName, out List<string> ModelInputPaths, out List<string> ModelOutputPaths)
        {

            Error _Error;

            ModelOutputPaths = new List<string>();
            ModelInputPaths = new List<string>();

            if (!File.Exists(ModelFile))
            {
                Console.WriteLine("Cannot find the specified model: " + ModelFile);
                return Failure;
            }

            // Add a simulation model to the target
            SimulationModels _SimulationModels = _Target.GetSimulationModels();

            Models _Models = _SimulationModels.GetModels();

            //Warning: "Model" initializer relies on 32 bit application.  Exception generated for 64 bit applications. Forum post describing same issue.
            // https://forums.ni.com/t5/Additional-NI-Software-Idea/Provide-a-VeriStand-SystemDefinitionAPI-that-is-usable-from/idi-p/3694398?profile.language=en
            // Workaround - use assemblies from C:\Program Files\National Instruments\VeriStand 2019 and include MDLWrapExe as reference.
            // Optional TODO: Use different assemblies based on build type:
            //  https://docs.microsoft.com/en-us/visualstudio/ide/how-to-configure-projects-to-target-platforms?view=vs-2019
            //  https://stackoverflow.com/questions/3832552/conditionally-use-32-64-bit-reference-when-building-in-visual-studio
            Model _Model = null;

            try
            {
                _Model = new Model( BaseName, // Name
                                    "Imported PLECS model", // Description
                                    ModelFile, // Model Path
                                    -2, // Automatically Select Processor = -2, otherwise, use zero based processor number
                                    1, // Decimation
                                    0, // Initial State -- 0 = Running, 1 = Paused
                                    true, // Segment Vectors
                                    true, // Import Parameters
                                    true); // Import Signals

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error importing model into project. This error may be due to x86 vs x64 compatiability or invalid signal names.\n" + ex.Message);
                return Failure;
            }

            _Models.AddModel(_Model, out _Error);

            /* The GetInports() and GetOuptorts() results are not necessarily correctly sorted.
             * The current approach is to label each port with an id# at the end, similar to parameters.
             * Then a regex is used to determine the id#, create a list of IDs, and then sort the ports according to the list.
             * Some notes:
             *  Scalar signals are of the form inport_(0).
             *  Vectorized signals are converted to scalars and have IDs at the end - e.g. inport_(1)(1,1), inport(1)(2,1)
             *  The above results in the id# being insufficient to determine unique signals, so the list has to be re-indexed.
             *  For example, list may come back L=0,1,2,4,3,3 which is indexed in order by M=L(0,1,2,4,5,3)
             *  Some of the reindexing/sorting code is from: https://stackoverflow.com/questions/659866/is-there-c-sharp-support-for-an-index-based-sort

             */
            int portIndex = 0;
            Regex portIndexRegex = new Regex(@".+_\((?<PortID>\d+)\)(\(\d+,\d+\))?$"); //Array indices optional -->(optional expression)?

            Inports _Inports = _Model.GetInportsSection();
            Inport[] _InportList = _Inports.GetInports(true); //Boolean identifies deep list traverse
            List<int> _InportOrder = new List<int>();
            foreach (Inport _Inport in _InportList.AsEnumerable())
            {
                Match portMatch = portIndexRegex.Match(_Inport.Name);
                portIndex = int.Parse(portMatch.Groups["PortID"].Value);
                _InportOrder.Add(portIndex);
            }
            List<int> _InportOrderIdx = Enumerable.Range(0, _InportList.Length).ToList<int>();
            _InportOrderIdx.Sort( (a, b) => _InportOrder[a].CompareTo(_InportOrder[b]));
            foreach (int i in _InportOrderIdx.AsEnumerable())
            {
                ModelInputPaths.Add(_InportList[i].NodePath);
            }

            Outports _Outports = _Model.GetOutportsSection();
            Outport[] _OutportList = _Outports.GetOutports(true); //Boolean identifies deep list traverse
            List<int> _OutportOrder = new List<int>();
            foreach (Outport _Outport in _OutportList.AsEnumerable())
            {
                Match portMatch = portIndexRegex.Match(_Outport.Name);
                portIndex = int.Parse(portMatch.Groups["PortID"].Value);
                _OutportOrder.Add(portIndex);
            }
            List<int> _OutportOrderIdx = Enumerable.Range(0, _OutportList.Length).ToList<int>();
            _OutportOrderIdx.Sort((a, b) => _OutportOrder[a].CompareTo(_OutportOrder[b]));
            foreach (int i in _OutportOrderIdx.AsEnumerable())
            {
                ModelOutputPaths.Add(_OutportList[i].NodePath);
            }

            if (_Error.IsError)
            {
                Console.WriteLine("Error loading model: \n" + _Error.Message);
                return Failure;
            }

            return Success;
        }

        private static int AddHardware(Target _Target, SystemDefinition _SystemDefinition, SysDefWrapper.Registry Reg, SysDefWrapper.Hardware Hw, out List<string> HardwareInputPaths, out List<string> HardwareOutputPaths)
        {
            /*
            *    --Final order of packed model:
            *    --  inports = [Ain,Din,Cin,non-hardware ports]
            *    --  outports = [Aout,Dout,Cout,non-hardware ports
            */

            Error _Error = new Error();
            HardwareInputPaths = new List<string>();
            HardwareOutputPaths = new List<string>();
            DAQDevice _DAQDevice;

            Hardware _Hardware = _Target.GetHardware();

            //Get the reference to the DAQ section, assume 1 chassis.
            DAQ _DAQ = _Hardware.GetChassisList()[0].GetDAQ();

            // Add DAQ Devices - assume default configuration for analog inputs (written on per-channel basis)
            DAQDeviceInputConfiguration _DAQDeviceInputConfiguration;
            _DAQDeviceInputConfiguration = DAQDeviceInputConfiguration.Default;

            // Create access to scales folder, used for analog io scaling and counters.
            Scales _Scales = _SystemDefinition.Root.GetScales();

            string AnalogInScaleFolderName = "AnalogInScales";
            string AnalogOutScaleFolderName = "AnalogOutScales";
            string CounterOutScaleFolderName = "CounterOutScales";
            string CounterInScaleFolderName = "CounterInScales";

            ScaleFolder _AnalogInScaleFolder = new ScaleFolder(AnalogInScaleFolderName,"");
            ScaleFolder _AnalogOutScaleFolder = new ScaleFolder(AnalogOutScaleFolderName, "");
            ScaleFolder _CounterOutScaleFolder = new ScaleFolder(CounterOutScaleFolderName, "");
            ScaleFolder _CounterInScaleFolder = new ScaleFolder(CounterInScaleFolderName, "");


            if (Reg.NumAnalogInputs > 0)
            {
                _Scales.AddScaleFolder(_AnalogInScaleFolder, out _Error);
                ErrorEventHandler("Error creating AI Scales Folder", _Error);
            }
            if (Reg.NumAnalogOutputs > 0)
            {
                _Scales.AddScaleFolder(_AnalogOutScaleFolder, out _Error);
                ErrorEventHandler("Error creating AI Scales Folder", _Error);
            }
            if (Reg.NumCounterOutputs > 0)
            {
                _Scales.AddScaleFolder(_CounterOutScaleFolder, out _Error);
                ErrorEventHandler("Error creating CO Scales Folder", _Error);
            }
            if (Reg.NumCounterInputs > 0)
            {
                _Scales.AddScaleFolder(_CounterInScaleFolder, out _Error);
                ErrorEventHandler("Error creating CI Scales Folder", _Error);
            }

            //Retain list of slots and their index in the hardware array
            Dictionary<uint?, int> SlotList = new Dictionary<uint?, int>(); //Dict<slot,daq>

            int SlotIdx = 0;
            for (int i = 0; i < Hw.slotNums.Length; i++)
            {
                //Skip over any null entries
                if (Hw.slotNames[i] == null | Hw.slotProducts[i] == null | Hw.slotNums[i] == null)
                {
                    continue;
                }
                //Console.WriteLine("Name: " + Hw.slotNames[i] + " Product: " + Hw.slotProducts[i]);

                //Record in dictionary
                SlotList.Add(Hw.slotNums[i], SlotIdx);

                //Add device
                _DAQDevice = new DAQDevice(Hw.slotNames[i], //Name
                                           Hw.slotNames[i], //Description set to slot number
                                            _DAQDeviceInputConfiguration);  //Input Configuration

                _DAQDevice.ProductName = Hw.slotProducts[i];

                if (Hw.PXIBackplaneReferenceClock.Count < i || String.Equals(Hw.PXIBackplaneReferenceClock[i],"Automatic")) {_DAQDevice.BackplaneReferenceClock = PXIBackplaneReferenceClock.Automatic;}
                else if (String.Equals(Hw.PXIBackplaneReferenceClock[i], "None")) { _DAQDevice.BackplaneReferenceClock = PXIBackplaneReferenceClock.None; }
                else if (String.Equals(Hw.PXIBackplaneReferenceClock[i], "Clk10")) { _DAQDevice.BackplaneReferenceClock = PXIBackplaneReferenceClock.Clk10; }
                else if (String.Equals(Hw.PXIBackplaneReferenceClock[i], "Clk100")) { _DAQDevice.BackplaneReferenceClock = PXIBackplaneReferenceClock.Clk100; }

                _DAQ.AddDevice(_DAQDevice, out _Error); //Add the card to the sys def

                SlotIdx++;
                ErrorEventHandler("Error registering DAQ device", _Error);
            }

            //Retain list of devices
            DAQDevice[] _DAQDevices = _DAQ.GetDeviceList();

            //Add Analog Inputs
            for (int i = 0; i < Reg.NumAnalogInputs; i++)
            {
                SysDefWrapper.AIConfig cfg = Reg.AnalogInputConfigs[i];

                _DAQDevice = _DAQDevices[SlotList[cfg.slot]];

                //Look for inputs and create if it does not yet exist
                DAQAnalogInputs _DAQAnalogInputs = _DAQDevice.GetAnalogInputSection();
                if (_DAQAnalogInputs == null)
                {
                    _DAQAnalogInputs = _DAQDevice.CreateAnalogInputs(out _Error);
                    ErrorEventHandler("Error creating analog inputs section", _Error);
                }

                if (cfg.mode == 1) { _DAQDeviceInputConfiguration = DAQDeviceInputConfiguration.Default; }
                else if (cfg.mode == 2) { _DAQDeviceInputConfiguration = DAQDeviceInputConfiguration.RSE; }
                else if (cfg.mode == 3) { _DAQDeviceInputConfiguration = DAQDeviceInputConfiguration.NRSE; }
                else if (cfg.mode == 4) { _DAQDeviceInputConfiguration = DAQDeviceInputConfiguration.Differential; }
                else if (cfg.mode == 5) { _DAQDeviceInputConfiguration = DAQDeviceInputConfiguration.Pseudodifferential; }
                else
                {
                    Console.WriteLine("Invalid DAQ Device Analog Input Configuration " + cfg.mode.ToString() +
                        ".  Must be an integer between 1 and 5");
                    return Failure;
                }

                for (int j = 0; j < cfg.dim; j++)
                {
                    DAQAnalogInput _DAQAnalogInput = new DAQAnalogInput(
                        "AI" + cfg.channel[j].ToString(),         //name
                        cfg.channel[j],                         //channel #
                        DAQMeasurementType.AnalogInputVoltage,  //channel type
                        0);                                     //initial value

                    //Applied before any scaling factors. Differs from DAQmx
                    _DAQAnalogInput.HighLevel = cfg.max[j];     //set max val
                    _DAQAnalogInput.LowLevel = cfg.min[j];      //set min val

                    _DAQAnalogInputs.AddAnalogInput(_DAQAnalogInput, out _Error);
                    ErrorEventHandler("Error registering Analog Input " + cfg.channel[j], _Error);
                    HardwareInputPaths.Add(_DAQAnalogInput.NodePath);

                    //Create Scale {a0, a1} where y=a1*x+a0. 
                    if (cfg.scale[j] == 0.0) // Must calc reverse coeffs so prevent divide by zero error.
                    {
                        Console.WriteLine("Scale parameter must be greater than zero for block " + cfg.name + " channel " + cfg.channel[j].ToString());
                        return Failure;
                    }
                    double[] ForwardCoeffs = { cfg.offset[j], cfg.scale[j], };
                    double[] ReverseCoeffs = { -cfg.offset[j] / cfg.scale[j], 1.0 / cfg.scale[j] };
                    Scale _Scale = new PolynomialScale(cfg.name + "_" + _DAQAnalogInput.Name, ForwardCoeffs, ReverseCoeffs, "");
                    _AnalogInScaleFolder.AddScale(_Scale, out _Error);
                    ErrorEventHandler("Creating scale for analog input", _Error);

                    _DAQAnalogInput.Scale=_Scale;   //Associate scale with channel.
                }
            }


            //Add Analog Outputs
            for (int i = 0; i < Reg.NumAnalogOutputs; i++)
            {
                SysDefWrapper.AOConfig cfg = Reg.AnalogOutputConfigs[i];

                _DAQDevice = _DAQDevices[SlotList[cfg.slot]];

                //Look for inputs and create if it does not yet exist
                DAQAnalogOutputs _DAQAnalogOutputs = _DAQDevice.GetAnalogOutputSection();
                if (_DAQAnalogOutputs == null)
                {
                    _DAQAnalogOutputs = _DAQDevice.CreateAnalogOutputs(out _Error);
                    ErrorEventHandler("Error creating analog outputs section", _Error);
                }

                for (int j = 0; j < cfg.dim; j++)
                {
                    DAQAnalogOutput _DAQAnalogOutput = new DAQAnalogOutput(
                        "AO" + cfg.channel[j].ToString(),         //name
                        "V",                                    //units
                        0,                                      //initial value
                        cfg.min[j],                             //min val
                        cfg.max[j],                             //max val
                        cfg.channel[j],                         //channel #
                        DAQAnalogChannelType.Voltage);          //channel type

                    _DAQAnalogOutputs.AddAnalogOutput(_DAQAnalogOutput, out _Error);
                    ErrorEventHandler("Error registering Analog Output " + cfg.channel[j], _Error);
                    HardwareOutputPaths.Add(_DAQAnalogOutput.NodePath);

                    //Create Scale {a0, a1} where y=a1*x+a0. 
                    if (cfg.scale[j] == 0.0) // Must calc reverse coeffs so prevent divide by zero error.
                    {
                        Console.WriteLine("Scale parameter must be greater than zero for block " + cfg.name + " channel " + cfg.channel[j].ToString());
                        return Failure;
                    }
                    double[] ForwardCoeffs = { cfg.offset[j], cfg.scale[j], };
                    double[] ReverseCoeffs = { -cfg.offset[j] / cfg.scale[j], 1.0 / cfg.scale[j] };
                    Scale _Scale = new PolynomialScale(cfg.name+"_"+_DAQAnalogOutput.Name, ForwardCoeffs, ReverseCoeffs, "");
                    _AnalogOutScaleFolder.AddScale(_Scale, out _Error);
                    ErrorEventHandler("Creating scale for analog output", _Error);

                    _DAQAnalogOutput.Scale = _Scale;   //Associate scale with channel.
                }
            }


            //Add Digital Inputs 
            for (int i = 0; i < Reg.NumDigitalInputs; i++)
            {
                SysDefWrapper.DIConfig cfg = Reg.DigitalInputConfigs[i];
                _DAQDevice = _DAQDevices[SlotList[cfg.slot]];

                DAQDigitalInputs _DAQDigitalInputs = _DAQDevice.GetDigitalInputSection();
                if (_DAQDigitalInputs == null)
                {
                    _DAQDigitalInputs = _DAQDevice.CreateDigitalInputs(out _Error);
                    ErrorEventHandler("Error creating digital inputs section for Slot " + cfg.slot.ToString(), _Error);
                }

                //Get list of all ports and find one with matching name (use default port naming scheme). Construct one otherwise
                DAQDIOPort[] _DAQDIOPorts = _DAQDigitalInputs.GetDIOPorts();
                DAQDIOPort _DAQDIOPort = _DAQDIOPort = Array.Find(_DAQDIOPorts, p => p.Name == "port" + cfg.port.ToString());
                if (_DAQDIOPort == null)
                {
                    _DAQDIOPort = new DAQDIOPort(cfg.port, false);
                    _DAQDigitalInputs.AddDIOPort(_DAQDIOPort, out _Error);
                    ErrorEventHandler("Error adding digital input port section for Port " + cfg.port.ToString(), _Error);
                }

                for (int j = 0; j < cfg.dim; j++)
                {
                    DAQDigitalInput _DAQDigitalInput = new DAQDigitalInput(
                        "DI" + cfg.channel[j].ToString(),     //name
                        false,                              //initial value
                        cfg.channel[j],                     //channel #
                        cfg.port);                        //port #

                    _DAQDIOPort.AddDigitalInput(_DAQDigitalInput, out _Error);
                    ErrorEventHandler("Error registering Digital Input " + cfg.channel[j], _Error);
                    HardwareInputPaths.Add(_DAQDigitalInput.NodePath);
                }
            }

            //Add Digital Outputs
            for (int i = 0; i < Reg.NumDigitalOutputs; i++)
            {
                SysDefWrapper.DOConfig cfg = Reg.DigitalOutputConfigs[i];
                _DAQDevice = _DAQDevices[SlotList[cfg.slot]];

                DAQDigitalOutputs _DAQDigitalOutputs = _DAQDevice.GetDigitalOutputSection();
                if (_DAQDigitalOutputs == null)
                {
                    _DAQDigitalOutputs = _DAQDevice.CreateDigitalOutputs(out _Error);
                    ErrorEventHandler("Error creating digital outputs section for Slot " + cfg.slot.ToString(), _Error);
                }

                //Get list of all ports and find one with matching name (use default port naming scheme). Construct one otherwise
                DAQDIOPort[] _DAQDIOPorts = _DAQDigitalOutputs.GetDIOPorts();
                DAQDIOPort _DAQDIOPort = _DAQDIOPort = Array.Find(_DAQDIOPorts, p => p.Name == "port" + cfg.port.ToString());
                if (_DAQDIOPort == null)
                {
                    _DAQDIOPort = new DAQDIOPort(cfg.port, false);
                    _DAQDigitalOutputs.AddDIOPort(_DAQDIOPort, out _Error);
                    ErrorEventHandler("Error adding digital output port section for Port " + cfg.port.ToString(), _Error);
                }

                for (int j = 0; j < cfg.dim; j++)
                {
                    DAQDigitalOutput _DAQDigitalOutput = new DAQDigitalOutput(
                        "DO" + cfg.channel[j].ToString(),   //name
                        false,                              //initial value
                        cfg.channel[j],                     //channel #
                        cfg.port);                          //port #

                    _DAQDIOPort.AddDigitalOutput(_DAQDigitalOutput, out _Error);
                    ErrorEventHandler("Error registering Digital Output " + cfg.channel[j], _Error);
                    HardwareOutputPaths.Add(_DAQDigitalOutput.NodePath);

                }
            }

            //Add Counter Inputs 
            for (int i = 0; i < Reg.NumCounterInputs; i++)
            {
                SysDefWrapper.CIConfig cfg = Reg.CounterInputConfigs[i];

                _DAQDevice = _DAQDevices[SlotList[cfg.slot]];

                //Look for inputs and create if it does not yet exist
                DAQCounters _DAQCounters = _DAQDevice.GetCounterSection();
                               
                if (_DAQCounters == null)
                {
                    _DAQCounters = _DAQDevice.CreateCounters(out _Error);
                    ErrorEventHandler("Error creating counter inputs section", _Error);
                }

                if (cfg.counterType == "position") 
                {
                    DAQCounterDecoding _DAQCounterDecoding = new DAQCounterDecoding();
                    DAQCounterZIndexMode _DAQCounterZIndexMode = new DAQCounterZIndexMode();
                    double[] ForwardCoeffs = { 0.0, 0.0 };
                    double[] ReverseCoeffs = { 0.0, 0.0 };
                    if      (cfg.decoding == 1) { 
                        _DAQCounterDecoding = DAQCounterDecoding.Decoding1X;
                        ForwardCoeffs[1] = 1.0;
                        ReverseCoeffs[1] = 1.0;
                    }
                    else if (cfg.decoding == 2) { 
                        _DAQCounterDecoding = DAQCounterDecoding.Decoding2X;
                        ForwardCoeffs[1] = 2.0;
                        ReverseCoeffs[1] = 0.5;
                    }
                    else if (cfg.decoding == 3) {
                        _DAQCounterDecoding = DAQCounterDecoding.Decoding4X;
                        ForwardCoeffs[1] = 4.00;
                        ReverseCoeffs[1] = 0.25;
                    }
                    else
                    {
                        Console.WriteLine("Invalid decoding type for DAQ Device Counter Input " + cfg.name + ".");
                        return Failure;
                    }
                    if (cfg.indexMode == 1) { _DAQCounterZIndexMode = DAQCounterZIndexMode.AHighBHigh; }
                    else if (cfg.indexMode == 2) { _DAQCounterZIndexMode = DAQCounterZIndexMode.AHighBLow; }
                    else if (cfg.indexMode == 3) { _DAQCounterZIndexMode = DAQCounterZIndexMode.ALowBHigh; }
                    else if (cfg.indexMode == 4) { _DAQCounterZIndexMode = DAQCounterZIndexMode.ALowBLow; }
                    else
                    {
                        Console.WriteLine("Invalid Z index mode for DAQ Device Counter Input " + cfg.name + ".");
                        return Failure;
                    }

                    DAQPositionMeasurement _DAQPositionMeasurement = new DAQPositionMeasurement(
                        "CI"+cfg.ctr.ToString(),    //name
                        cfg.name,                   //description
                        cfg.ctr,                    //index
                        0,                          //default value
                        _DAQCounterDecoding,        //decoding type
                        _DAQCounterZIndexMode       //index reset mode
                        );
                    _DAQCounters.AddCounter(_DAQPositionMeasurement, out _Error);
                    HardwareInputPaths.Add(_DAQPositionMeasurement.NodePath);

                    //Add scale to account for counting behavior (1x=1c increment, 2x=0.5c increment, 4x=0.25c increment)
                    Scale _Scale = new PolynomialScale(cfg.name + "_" + _DAQPositionMeasurement.Name, ForwardCoeffs, ReverseCoeffs, "");
                    _CounterInScaleFolder.AddScale(_Scale, out _Error);
                    ErrorEventHandler("Creating scale for counter input", _Error);
                    _DAQPositionMeasurement.Scale = _Scale;   //count scale with channel.

                }
                else if (cfg.counterType == "edge") 
                {
                    DAQCounterCountMode _DAQCounterCountMode = new DAQCounterCountMode();
                    DAQCounterEdge _DAQCounterEdge = new DAQCounterEdge();

                    if      (cfg.direction == 1) { _DAQCounterCountMode = DAQCounterCountMode.Up; }
                    else if (cfg.direction == 2) { _DAQCounterCountMode = DAQCounterCountMode.Down; }
                    else if (cfg.direction == 3) { _DAQCounterCountMode = DAQCounterCountMode.ExternallyControlled; }
                    else
                    {
                        Console.WriteLine("Invalid counting direction for DAQ Device Counter Input " + cfg.name + ".");
                        return Failure;
                    }

                    if      (cfg.edge == 1) { _DAQCounterEdge = DAQCounterEdge.Rising; }
                    else if (cfg.edge == 2) { _DAQCounterEdge = DAQCounterEdge.Falling; }
                    else
                    {
                        Console.WriteLine("Invalid edge type for DAQ Device Counter Input " + cfg.name + ".");
                        return Failure;
                    }

                    //The "ResetVariable" value is required and cannot be empty in constructor.  
                    //      A dummy User Channel is used as a workaround to prevent an error message with the max error.
                    UserChannels _UserChannels = _Target.GetUserChannels();
                    UserChannel _UserChannel = _UserChannels.AddNewUserChannel("CI" + cfg.ctr.ToString(), "Edge counter reset dummy user channel","", 0, out _Error); //max
                    BaseNode _BaseNode = NodeIDUtil.IDToBaseNode(_UserChannel.NodeID);

                    DAQCountUpDown _DAQCountUpDown = new DAQCountUpDown(
                        "CI" + cfg.ctr.ToString(),  //name
                        cfg.name,                   //description
                        cfg.ctr,                    //index
                        (double)cfg.init,                          //default value
                        _DAQCounterCountMode,       //count mode (up/down)
                        _DAQCounterEdge,            //edge type (rising/falling)
                        _BaseNode
                        );
                    _DAQCounters.AddCounter(_DAQCountUpDown, out _Error);
                    HardwareInputPaths.Add(_DAQCountUpDown.NodePath);
                }

                else
                {
                    Console.WriteLine("Invalid DAQ Device Counter Input Configuration " + cfg.counterType +
                        ".  Must be a position or edge counting type.  Other types not supported.");
                    return Failure;
                }

                ErrorEventHandler("Error registering Counter Input " + cfg.name, _Error);
            }

            //Add Counter Outputs
            for (int i = 0; i < Reg.NumCounterOutputs; i++)
            {
                SysDefWrapper.COConfig cfg = Reg.CounterOutputConfigs[i];

                _DAQDevice = _DAQDevices[SlotList[cfg.slot]];

                //Look for inputs and create if it does not yet exist
                DAQCounters _DAQCounters = _DAQDevice.GetCounterSection();
                if (_DAQCounters == null)
                {
                    _DAQCounters = _DAQDevice.CreateCounters(out _Error);
                    ErrorEventHandler("Error creating counter inputs section", _Error);
                }

                //Constructor does not give all options - have to edit post creation.
                DAQPulseGeneration _DAQPulseGeneration = new DAQPulseGeneration(
                        "CO" + cfg.ctr.ToString(),  //name
                        cfg.name,                   //description
                        cfg.ctr                    //index
                        );

                //Have to set some properties manually: https://zone.ni.com/reference/en-XX/help/372846M-01/vsnetapis/daq_props_table/#pulsegen
                if (cfg.polarity == 1) 
                {
                    _DAQPulseGeneration.SetEnumProperty("Idle State", 10192); //low
                }
                else 
                {
                    _DAQPulseGeneration.SetEnumProperty("Idle State", 10214); //high 
                }
                _DAQPulseGeneration.SetBooleanProperty("isHWTSP", false);
                _DAQPulseGeneration.SetDoubleProperty("Init Delay",cfg.ph/cfg.fc);
                //TODO: Not supported/available? https://forums.ni.com/t5/NI-VeriStand/Synchronized-DAQ-Counter-Pulse-Generation/td-p/4136959
                //Use digital edge to synchronize 
                //_DAQPulseGeneration.SetI32Property("StartTrig.Type", 1); 

                //TODO: The approach used to set the initial values here is not working. Related to above note.
                _DAQPulseGeneration.DataChannels[0].DefaultValue[0] = cfg.fc;
                _DAQPulseGeneration.DataChannels[1].DefaultValue[0] =0.0; //Same initial value as per custom engine.

                _DAQCounters.AddCounterOutput(_DAQPulseGeneration,out _Error);
                ErrorEventHandler("Error registering Counter Output " + cfg.name, _Error);
                HardwareOutputPaths.Add(_DAQPulseGeneration.NodePath + "/Duty Cycle");
                HardwareOutputPaths.Add(_DAQPulseGeneration.NodePath + "/Frequency");

                //Add scale to account for polarity flag
                double[] ForwardCoeffs = { 0.0, 1.0 };
                double[] ReverseCoeffs = { 0.0, 1.0 };
                if (cfg.polarity != 1)
                {
                    ForwardCoeffs[0] = 1.0;
                    ForwardCoeffs[1] = -1.0;
                    ReverseCoeffs[0] = 1.0;
                    ReverseCoeffs[1] = -1.0;
                }
                Scale _Scale = new PolynomialScale(cfg.name + "_" + _DAQPulseGeneration.Name + "_Duty", ForwardCoeffs, ReverseCoeffs, "");
                _CounterOutScaleFolder.AddScale(_Scale, out _Error);
                ErrorEventHandler("Creating scale for counter output", _Error);

                _DAQPulseGeneration.DataChannels[1].Scale = _Scale;   //Duty scale with channel.
                


            }

            return Success;
        }

        private static int MapModelToHardware(SystemDefinition _SystemDefinition, List<string> ModelInputPaths, List<string> ModelOutputPaths, List<string> HardwareInputPaths, List<string> HardwareOutputPaths)
        {
            Error _Error;
            //Truncate the model IO to match the number of hardware IO
            int NumHardwareInputs = HardwareInputPaths.Count;
            int NumHardwareOutputs = HardwareOutputPaths.Count;
            int NumModelInputs = ModelInputPaths.Count;
            int NumModelOutputs = ModelOutputPaths.Count;

            //Console.WriteLine(String.Format("HWIN {0} HWOUT {1} MDLIN {2}  MDLOUT {3}", NumHardwareInputs,NumHardwareOutputs,NumModelInputs,NumModelOutputs));
            if((NumModelInputs - NumHardwareInputs < 0) | (NumModelOutputs - NumHardwareOutputs < 0))
            {
                Console.WriteLine("There are more hardware IO ports assigned than model IO.  This indicates a potential model mismatch or import error.");
                return Failure;
            }

            try
            {
                ModelInputPaths.RemoveRange(NumHardwareInputs, NumModelInputs - NumHardwareInputs);
                ModelOutputPaths.RemoveRange(NumHardwareOutputs, NumModelOutputs - NumHardwareOutputs);
            }
            catch(Exception ex)
            {
                Console.WriteLine("Error aligning model IO and hardware IO .\n" + ex.Message);
                return Failure;
            }

            /*
            foreach (string str in ModelInputPaths)
            {
                Console.WriteLine("ModelInput:\t" + str);
            }
            foreach (string str in HardwareInputPaths)
            {
                Console.WriteLine("HardwareInput:\t" + str);
            }
            */
            _SystemDefinition.Root.AddChannelMappings(HardwareInputPaths.ToArray(), ModelInputPaths.ToArray(),out _Error);
            ErrorEventHandler("Error mapping hardware inputs to model inputs", _Error);

            _SystemDefinition.Root.AddChannelMappings(ModelOutputPaths.ToArray(), HardwareOutputPaths.ToArray(), out _Error);
            ErrorEventHandler("Error mapping model outputs to hardware outputs", _Error);

            return Success;
        }


        private static bool ErrorEventHandler(string MyMessage, Error _Error)
        {
            if (_Error.IsError)
            {
                Console.WriteLine(MyMessage);
                Console.WriteLine(_Error.Message);
                return true;
            }
            return false;
        }



    }
}
