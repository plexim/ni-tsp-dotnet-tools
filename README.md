# Background
The NI VeriStand Target Support Package requires an auxiliary application to serve as a bridge between the PLECS application and the NI VeriStand toolchain.  NI provides a set of [.NET API's](https://www.ni.com/docs/en-US/bundle/veristand-net-api-reference/page/vsnetapis/bp_vsnetapis.html) to programatically interact with and control the VeriStand software. VeriStand also uses the [ASAM XIL](https://www.ni.com/docs/en-US/bundle/veristand-21/page/asam-xil-interface.html) interface and associated API's to control the real-time application.

# Using the application via the command line
One can interact with the application through the following command line options

Generate a Veristand project from the PLECS model:

    plx-asam-xil-tool SystemDefinition -b=Subsystem -p=..\\ni\\tsp\\src\\veritarget\\cg -f=..\\ni\\tsp\\build\\veritarget\\libmodel.so
    -b|base-name: The basename of the target model
    -f|model-file: Full path to the compiled model file (*.so)
    -p|codegen-path: The path to the model codegen directory


Load the model on the the RT target:

    plx-asam-xil-tool LoadModel  -c=PortConfig.xml -m=Subsystem -v=2020
    -c|config-file: The full path of the configuration file
    -m|model-name:  Name of simulation model as defined in System Explorer
    -v|veristand-version: VeriStand version identifier for TestBench

    Optionally:
    -p|project-file: Path to VeriStand project file (*.nivsproj).

Start the external mode:

    plx-asam-xil-tool ExtModeServer -c=PortConfig.xml -m=Subsystem
    -c|config-file: The full path of the configuration file
    -m|model-name:  Name of simulation model as defined in System Explorer
    -v|veristand-version: VeriStand version identifier for TestBench

    Optionally, you can also apply:
    -k|keep-alive: Don't quit after external mode disconnect. Keeps the server alive after PLECS disconnects (you then have to stop it using Ctrl-C)
    -a|non-intrusive: Attached to a running model (does not force a model reload, but will fail if the target is running a different model (or none)).
    -l|log-file: Redirect console output to file.

Configure generated code to use parameters from the VeriStand engine (note this call is used to build the model (*.so) and is required for all workflows):

    plx-asam-xil-tool CodeGenParameterModifier -b=Subsystem -p=..\\ni\\tsp\\src\\veritarget\\cg
    -b|base-name: The basename of the target model
    -p|codegen-path: The path to the model codegen directory


# Building the application
## Required software
The application is built in MS Visual Studio 2019. The Microsoft .NET Framework 4.6.2 or .NET Framework 4.7.2 must be installed (depending on the target VeriStand version).

## Builder tool
To build the application for deployment use run the commands below.  The commands assume Visual Studio 2019 is installed in the default installation directory.

    git clone https://gitlab.plexim.com/ni/ni-tsp-dotnet-tools
    python ni-tsp-dotnet-tools/tools/builder.py

Each release build should be tagged prior to distribution.

    git tag -a v1.0.1 -m "my version 1.0.1"
    git push origin v1.0.1
    
As a final step the builder tool will generate a zip archive titled `dnetTools_v1.0.1.zip`.

## Using the application with the target support package
The above command line calls are incorporated into the NI Target Support Package framework.  If updates are made to the application, then replace the contents of the `./NI_VeriStand/tools/dnettools` directory with the latest application version. Changes to the NI Target Support Package may be required if additional functionality as added.

If using the builder script, this would be the contents of the generated `dnetTools_v1.0.1.zip` archive.

For debugging, one can create symbolic links on windows to the debug/release directories by opening an admin command prompt window and entering the following: `mklink /D dnettools "absolute_path_to_desired_directory"`


