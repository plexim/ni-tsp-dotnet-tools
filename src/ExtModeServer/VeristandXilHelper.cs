using System;
using System.Collections.Generic;
using System.Linq;
using ASAM.XIL.Implementation.TestbenchFactory.Testbench;
using ASAM.XIL.Interfaces.Testbench;
using ASAM.XIL.Interfaces.Testbench.Common.CaptureResult;
using ASAM.XIL.Interfaces.Testbench.Common.MetaInfo;
using ASAM.XIL.Interfaces.Testbench.Common.ValueContainer;
using ASAM.XIL.Interfaces.Testbench.MAPort;
using NationalInstruments.VeriStand.ClientAPI;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Plexim.dNetTools.PlxAsamXilTool
{
    class VeristandXilHelper
    {
        public static ITestbench GetTestbench(string aVeriStandProductVersion = "2019.0.0")
        {
            if (Process.GetProcessesByName("VeriStand").Length <= 0)
            {
                return null;
            }

            ITestbenchFactory testbenchFactory = new TestbenchFactory();
            ITestbench testbench = null;
            try
            {
                testbench = testbenchFactory.CreateVendorSpecificTestbench(
                    vendorName: "National Instruments",
                    productName: "NI VeriStand ASAM XIL Interface",
                    productVersion: aVeriStandProductVersion);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetTestBench() resulted in Exception: " + ex.Message);
                return null;
            }
            return testbench;
        }

        public static IMAPort GetMAPort(ITestbench aTestbench, string aPortConfigFile = @"PortConfig.xml", bool aForceModelLoad = true)
        {
            IMAPortFactory maportFactory = aTestbench.MAPortFactory;
            IMAPort maport = maportFactory.CreateMAPort("plxsym");

            // Create configuration, which loads the .xml file defining the project to be loaded to the target
            IMAPortConfig maportConfig = null;
            try
            {
                maportConfig = maport.LoadConfiguration(aPortConfigFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetMAPort() LoadConfiguration resulted in Exception: " + ex.Message);
                return null;
            }
            if (maportConfig == null)
            {
                return null;
            }

            try
            {
                maport.Configure(maportConfig, aForceModelLoad);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetMAPort() Configure resulted in Exception: " + ex.Message);
                return null;
            }
            return maport;
        }

        public static void OpenProject(string aVeriStandProjectFile)
        {
            try
            {
                Factory factory = new Factory();
                IWorkspace2 iWorkspace = factory.GetIWorkspace2();
                DeployOptions deployOptions = new DeployOptions();
                deployOptions.DeploySystemDefinition = true;
                deployOptions.DoNotStartModels = true;
                iWorkspace.ConnectToSystem(aVeriStandProjectFile, deployOptions);
                //iWorkspace.AsyncDisconnectFromSystem("", true);  //Undeploy system definition.
                //IProject iProject = factory.GetIProject("localhost",aVeriStandProjectFile,"","");
                //iProject.Visible = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("OpenProject() to display project in VeriStand resulted in Exception: " + ex.Message);
            }  
        }

        public static List<string> GetChannelSubset(IList<string> aInputChannels, Regex aRgx)
        {
            // Create Regex's to find parameter and signal channels names
            List<string> OutChannels = new List<string>();
            foreach (string InputChannel in aInputChannels)
            {
                if (aRgx.IsMatch(InputChannel))
                {
                    OutChannels.Add(InputChannel);
                }
            }

            //Check Signals and parameters
            Console.WriteLine(string.Format("There were {0}  found:", OutChannels.Count));
            foreach (string OutChannel in OutChannels)
            {
                Console.WriteLine(string.Format("\t{0}", OutChannel));
            }
            return OutChannels;
        }

        public static string GetPrimaryTask(IMAPort aMAPort)
        {
            // First print list of tasks (imported from PortConfig.xml, where TaskXXX is task name and XXX is task frequency in Hz)
            IList<ITaskInfo> taskInfos = aMAPort.TaskInfos;
            Console.WriteLine(string.Format("There are {0} tasks available.", taskInfos.Count));
            foreach (ITaskInfo taskInfo in taskInfos)
            {
                Console.WriteLine(string.Format("\t task {0}: period {1} s ", taskInfo.Name, taskInfo.Period));
            }
            // PortConfig.xml is autogenerated and ensures one task is returned.
            return taskInfos[0].Name;
        }

        public static void PrintCaptureResult(ICaptureResult aCaptureResult)
        {

            Console.WriteLine("Capture result dump:");
            Console.WriteLine(string.Format("\tCapture start time {0}: {1}",
                aCaptureResult.CaptureStartTime,
                new DateTime(1970, 1, 1).AddSeconds(aCaptureResult.CaptureStartTime)));

            foreach (string channelGroup in aCaptureResult.SignalGroupNames)
            {

                Console.WriteLine("\n\t\tData dump:");
                ISignalGroupValue signalGroupValue = aCaptureResult.GetSignalGroupValue(channelGroup);
                Console.Write("\t\t");
                for (int i = 0; i < signalGroupValue.XVector.Count; ++i)
                {
                    Console.Write(string.Format("{0,10}", i));
                }
                Console.WriteLine(string.Empty);

                Console.Write("\t\t");
                foreach (double val in ((IFloatVectorValue)signalGroupValue.XVector).Value)
                {
                    Console.Write(string.Format("{0,10}", val.ToString("F3")));
                }
                Console.WriteLine(" (Time)");

                foreach (IVectorValue vectorValue in signalGroupValue.YVectors)
                {
                    Console.Write("\t\t");
                    foreach (double val in ((IFloatVectorValue)vectorValue).Value)
                    {
                        Console.Write(string.Format("{0,10}", val.ToString("F3")));
                    }
                    Console.WriteLine(string.Format(" ({0})", vectorValue.Attributes.Name.Split('/').Last()));
                }
            }
        }
    }
}
