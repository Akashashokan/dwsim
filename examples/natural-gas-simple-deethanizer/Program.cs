using System;
using System.IO;

internal static class Program
{
    /// <summary>
    /// Usage:
    ///   GenerateFlowsheet.exe [outputFile]
    /// </summary>
    private static int Main(string[] args)
    {
        string output = args.Length > 0
            ? args[0]
            : Path.Combine(Environment.CurrentDirectory, "natural-gas-simple-deethanizer.dwxmz");

        NaturalGasSimpleFlowsheet.Generate(output);
        Console.WriteLine($"Flowsheet generated: {output}");
        return 0;
    }
}
