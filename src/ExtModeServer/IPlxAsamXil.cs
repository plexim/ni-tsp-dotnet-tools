namespace Plexim.dNetTools.PlxAsamXilTool
{
    interface IPlxAsamXil
    {
        bool Run();

        bool StartSimulation();

        bool StopSimulation();

        bool StartExtModeServer();

        void PreConnect(string aVeriStandProjectFile);

        void Cancel();

        void Join();
    }
}
