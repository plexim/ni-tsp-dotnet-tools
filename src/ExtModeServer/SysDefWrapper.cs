using System.Collections.Generic;


namespace Plexim.dNetTools.PlxAsamXilTool
{
    class SysDefWrapper
    {
        public class Registry
        {
            public int NumAnalogInputs { get; set; }
            public IList<AIConfig> AnalogInputConfigs { get; set; }
            public int NumAnalogOutputs { get; set; }
            public IList<AOConfig> AnalogOutputConfigs { get; set; }
            public int NumDigitalInputs { get; set; }
            public IList<DIConfig> DigitalInputConfigs { get; set; }
            public int NumDigitalOutputs { get; set; }
            public IList<DOConfig> DigitalOutputConfigs { get; set; }
            public int NumCounterInputs { get; set; }
            public IList<CIConfig> CounterInputConfigs { get; set; }
            public int NumCounterOutputs { get; set; }
            public IList<COConfig> CounterOutputConfigs { get; set; }
        }

        public class Hardware
        {
            public IList<string> slotNames { get; set; }
            public IList<string> slotProducts { get; set; }
            public IList<string> PXIBackplaneReferenceClock { get; set; }
            public uint?[] slotNums { get; set; } //? is to accept null value.
            public uint? slotCount { get; set; } //? is to accept null value.
            public double[] aiMinMaxVal { get; set; }
            public double[] aoMinMaxVal { get; set; }
            public string targIP { get; set; }
            public string targUserName { get; set; }
            public string targPassword { get; set; }
            public double targRate { get; set; }
        }

        public class AIConfig
        {
            public string name { get; set; }
            public uint slot { get; set; }
            public uint dim { get; set; }
            public double dataType { get; set; }
            public uint mode { get; set; }
            public double[] scale { get; set; }
            public double[] offset { get; set; }
            public double[] max { get; set; }
            public double[] min { get; set; }
            public uint[] channel { get; set; }
        }
        public class AOConfig
        {
            public string name { get; set; }
            public uint slot { get; set; }
            public uint dim { get; set; }
            public double dataType { get; set; }
            public double[] scale { get; set; }
            public double[] max { get; set; }
            public double[] min { get; set; }
            public double[] offset { get; set; }
            public uint[] channel { get; set; }
        }
        public class DIConfig
        {
            public string name { get; set; }
            public uint slot { get; set; }
            public uint dim { get; set; }
            public uint port { get; set; }
            public double dataType { get; set; }
            public uint[] channel { get; set; }
        }
        public class DOConfig
        {

            public string name { get; set; }
            public uint slot { get; set; }
            public int dim { get; set; }
            public uint port { get; set; }
            public double dataType { get; set; }
            public uint[] channel { get; set; }
        }
        public class CIConfig
        {
            public string name { get; set; }
            public uint slot { get; set; }
            public uint dim { get; set; }
            public uint ctr { get; set; }
            public double dataType { get; set; }
            public uint?[] channel { get; set; }
            public string counterType { get; set; }
            //encoder/angle sensor - ? is to accept null value.
            public uint? indexMode { get; set; }
            public uint? reset { get; set; }
            public uint? decoding { get; set; }
            //edge counter - ? is to accept null value.
            public uint? edge { get; set; }
            public uint? direction { get; set; }
            public uint? dirChannel { get; set; }
            public double? init { get; set; } //double even though uint32.
        }
        public class COConfig
        {
            public string name { get; set; }
            public uint slot { get; set; }
            public uint dim { get; set; }
            public uint ctr { get; set; }
            public double dataType { get; set; }
            public uint?[] channel { get; set; }
            public uint polarity { get; set; }
            public double fc { get; set; }
            public double ph { get; set; }
        }
    }
}