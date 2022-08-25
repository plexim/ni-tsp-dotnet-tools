using System;
using System.IO;
using System.Collections.Generic;
using ManyConsole;

namespace Plexim.dNetTools.PlxAsamXilTool
{
    class Program
    {
        public class LoadModelCommand : ConsoleCommand
        {
            private const int Success = 0;
            private const int Failure = 1;

            public string ConfigFile { get; set; }
            public string SimulationModel { get; set; }
            public string VeriStandProductVersion { get; set; }
            public string VeriStandProjectFile { get; set; }


            public LoadModelCommand()
            {
                // Register the actual command with a simple (optional) description.
                IsCommand("LoadModel", "Loads NI VeriStand model");

                // Add a longer description for the help on that specific command.
                HasLongDescription("This can be used to load a VeriStandModel and (optionally) "+
                    "start its execution");

                // Required options/flags, append '=' to obtain the required value.
                HasRequiredOption("c|config-file=", "The full path of the configuration file.", p => ConfigFile = p);
                HasRequiredOption("m|model-name=", "Name of simulation model as defined in System Explorer.", p => SimulationModel = p);
                HasRequiredOption("v|veristand-version=", "VeriStand version identifier for TestBench.", p => VeriStandProductVersion = p);
                HasOption("p|project-file:", "Path to VeriStand project file (*.nivsproj).", t => VeriStandProjectFile = t);
            }

            public override int Run(string[] remainingArguments)
            {
                try
                {
                    int result = Success;

                    IPlxAsamXil xil = new VeristandXil
                    {
                        PortConfigFile = ConfigFile,
                        SimulationModel = SimulationModel,
                        VeriStandProductVersion = VeriStandProductVersion,
                        VeriStandProjectFile = VeriStandProjectFile
                    };

                    if (!string.IsNullOrWhiteSpace(VeriStandProjectFile))
                    {
                        xil.PreConnect(VeriStandProjectFile);
                    }

                    if (!xil.Run())
                    {
                        Console.WriteLine("Unable to connect to VeriStand.");
                        result = Failure;
                    }
                    else if (!xil.StartSimulation())
                    {
                        Console.WriteLine("Unable to start simulation.");
                        result = Failure;
                    }
                    xil.Cancel();
                    xil.Join();
                    return result;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Console.Error.WriteLine(ex.StackTrace);

                    return Failure;
                }
            }
        }

        public class ExtModeServerCommand : ConsoleCommand
        {
            private const int Success = 0;
            private const int Failure = 1;

            public string ConfigFile { get; set; }
            public string SimulationModel { get; set; }
            public string VeriStandProductVersion { get; set; }
            public bool NonIntrusive { get; set; } = false;
            public bool KeepAlive { get; set; } = false;
            public string LogFile { get; set; }

            public ExtModeServerCommand()
            {
                // Register the actual command with a simple (optional) description.
                IsCommand("ExtModeServer", "PLECS External Mode Server for NI Veristand");

                // Add a longer description for the help on that specific command.
                HasLongDescription("This can be used to create a bridge between ASAM XIL and the PLECS external mode protocol.");

                // Required options/flags, append '=' to obtain the required value.
                HasRequiredOption("c|config-file=", "The full path of the configuration file.", p => ConfigFile = p);
                HasRequiredOption("m|model-name=", "Name of simulation model as defined in System Explorer.", p => SimulationModel = p);
                HasRequiredOption("v|veristand-version=", "VeriStand version identifier for TestBench.", p => VeriStandProductVersion = p);

                // Optional options/flags, append ':' to obtain an optional value, or null if not specified.
                HasOption("a|non-intrusive:", "Non intrusive Veristand attach.",
                    t => NonIntrusive = t == null ? true : Convert.ToBoolean(t));
                HasOption("k|keep-alive:", "Don't quit after external mode disconnect.",
                    t => KeepAlive = t == null ? true : Convert.ToBoolean(t));
                HasOption("l|log-file:", "Redirect console output to file.", t => LogFile = t);

            }

            public override int Run(string[] remainingArguments)
            {
                try
                {
                    int result = Success;

                    if (!string.IsNullOrWhiteSpace(LogFile))
                    {
                        StreamWriter streamWriter = new StreamWriter(LogFile, true); //True implies append
                        streamWriter.AutoFlush = true;
                        Console.SetOut(streamWriter);
                        Console.SetError(streamWriter);
                        Console.WriteLine("------------------------------------------------------------");
                        Console.WriteLine($"-{DateTime.Now.ToLongTimeString()} {DateTime.Now.ToLongDateString()}-");
                        Console.WriteLine("------------------------------------------------------------");
                    }

                    IPlxAsamXil xil = new VeristandXil
                    {
                        PortConfigFile = ConfigFile,
                        SimulationModel = SimulationModel,
                        VeriStandProductVersion = VeriStandProductVersion,
                        NonIntrusive = NonIntrusive,
                        KeepAlive = KeepAlive
                    };

                    if (!xil.Run())
                    {
                        Console.WriteLine("Unable to connect to VeriStand.");
                        result = Failure;
                        xil.Cancel();
                    }
                    /*
                    else if (!xil.StopSimulation())
                    {
                        Console.WriteLine("Unable to stop simulation.");
                        xil.Cancel();
                    }
                    */
                    else if (!xil.StartSimulation())
                    {
                        Console.WriteLine("Unable to start simulation.");
                        result = Failure;
                        xil.Cancel();
                    }
                    else if (!xil.StartExtModeServer())
                    {
                        Console.WriteLine("Unable to start external mode server.");
                        result = Failure;
                        xil.Cancel();
                    }
                    else
                    {

                        Console.WriteLine("");
                        Console.WriteLine($"XIL interface running and ready for external mode at {DateTime.Now.ToLongTimeString()} - abort with Ctrl-C.");

                        Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
                        {
                            e.Cancel = true;
                            xil.Cancel();
                        };
                    }
                    xil.Join();

                    return result;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Console.Error.WriteLine(ex.StackTrace);
                    return Failure;
                }
            }
        }

        public class SystemDefinitionCommand : ConsoleCommand
        {
            private const int Success = 0;
            private const int Failure = 1;

            public string BaseName { get; set; }
            public string ModelFile { get; set; }
            public string CodeGenPath { get; set; }


            public SystemDefinitionCommand()
            {

                // Register the actual command with a simple (optional) description.
                IsCommand("SystemDefinition", "Provides access to configure NI VeriStand model system definition");

                // Add a longer description for the help on that specific command.
                HasLongDescription("This can be used to configure an NI Veristand system definition, " +
                    "load a generated model, add IO, and map IO to the model.");

                // Required options/flags, append '=' to obtain the required value.

                HasRequiredOption("b|base-name=", "The basename of the target model.", p => BaseName = p);

                HasRequiredOption("f|model-file=", "Full path to the compiled model file (*.so).", p => ModelFile = p);

                HasRequiredOption("p|codegen-path=", "The path to the model codegen directory.", p => CodeGenPath = p);

            }

            public override int Run(string[] remainingArguments)
            {
                try
                {
                    int result = Success;

                    result = VeristandSysDef.Generate(BaseName,ModelFile,CodeGenPath);

                    return result;

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Console.Error.WriteLine(ex.StackTrace);

                    return Failure;
                }
            }
        }
        public class CodegenParameterModifier : ConsoleCommand
        {
            private const int Success = 0;
            private const int Failure = 1;

            public string BaseName { get; set; }
            public string CodeGenPath { get; set; }


            public CodegenParameterModifier()
            {
                // Register the actual command with a simple (optional) description.
                IsCommand("CodegenParameterModifier", "Configures the generated code to use parameters from the Veristand engine.");

                // Add a longer description for the help on that specific command.
                HasLongDescription("This tool is used to modify the base_name.c file to refer to parameters from"+
                    "the Veristand engine instead of the parameter structure in the generated code.");

                // Required options/flags, append '=' to obtain the required value.

                HasRequiredOption("b|base-name=", "The basename of the target model.", p => BaseName = p);

                HasRequiredOption("p|codegen-path=", "The path to the model codegen directory.", p => CodeGenPath = p);

            }

            public override int Run(string[] remainingArguments)
            {
                try
                {
                    /*
                    *  Modifying the |>BASE_NAME<|.c file so that Veristand Parameters map to parameters 
                    *  in the |>BASE_NAME<|_step() function. This is done through a wholesale search and replace since 
                    *  both datasets have the same format.  E.G "Controller_P."-->"readParam"
                    */

                    int result = Success;

                    string SourceFile = Path.Combine(CodeGenPath, BaseName + ".c");
                    string DestinationFile = Path.Combine(CodeGenPath, BaseName + "_plx.c");

                    if (!File.Exists(SourceFile))
                    {
                        Console.WriteLine("Cannot find generated code: " + SourceFile);
                        return Failure;
                    }

                    string[] SourceContents = File.ReadAllLines(SourceFile);
                    List<string> DestinationContents = new List<string>();

                    for (int i = 0; i < SourceContents.Length; i++)
                    {
                        /* Do not replace BaseName_P with readParam within the list of external mode parameters.  Otherwise compilation errors*/
                        if (SourceContents[i].Contains("#if defined(EXTERNAL_MODE) && EXTERNAL_MODE"))
                        {
                            while (!SourceContents[i].Contains("#endif"))
                            {
                                DestinationContents.Add(SourceContents[i]);
                                i++;
                            }
                            DestinationContents.Add(SourceContents[i]); /* #endif line */
                        }
                        else
                        {
                            DestinationContents.Add(SourceContents[i].Replace(BaseName + "_P.", "readParam."));
                        }
                        
                    }
                    File.WriteAllLines(DestinationFile, DestinationContents.ToArray());
                    return result;

                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Console.Error.WriteLine(ex.StackTrace);

                    return Failure;
                }
            }
        }


        public static int Main(string[] args)
        {
            var commands = GetCommands();

            return ConsoleCommandDispatcher.DispatchCommand(commands, args, Console.Out);
        }

        public static IEnumerable<ConsoleCommand> GetCommands()
        {
            return ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(typeof(Program));
        }
    }
}
