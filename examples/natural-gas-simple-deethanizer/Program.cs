using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    /// <summary>
    /// Entry point for the natural gas simple deethanizer flowsheet generator.
    ///
    /// Usage:
    ///   GenerateFlowsheet.exe [outputFile]
    ///
    /// If no argument is given the output is written as
    /// "natural-gas-simple-deethanizer.dwxmz" inside the example directory.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string exampleDir = Path.GetFullPath(
                Path.Combine(exeDir, @"..\..\..\..\examples\natural-gas-simple-deethanizer"));

            string output = Path.GetFullPath(
                args.Length > 0
                    ? args[0]
                    : Path.Combine(exampleDir, "natural-gas-simple-deethanizer.dwxmz"));

            Directory.SetCurrentDirectory(exeDir);

            Console.WriteLine("[GenerateFlowsheet] Initializing DWSIM Automation...");
            var automation = new DWSIM.Automation.Automation3();

            Console.WriteLine("[GenerateFlowsheet] Building natural-gas-simple-deethanizer flowsheet...");
            NaturalGasSimpleFlowsheet.Generate(output);

            Console.WriteLine("[GenerateFlowsheet] Flowsheet saved to: " + output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[GenerateFlowsheet] ERROR: " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
