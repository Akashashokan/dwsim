using System;
using System.IO;
using System.Reflection;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            Console.WriteLine("[GenerateFlowsheet] Initializing DWSIM Automation...");

            string examplesDir = LocateExamplesDir();
            Environment.CurrentDirectory = examplesDir;

            string outputFile = Path.Combine(examplesDir, "acid-gas-removal-dynamics.dwxmz");

            Console.WriteLine("[GenerateFlowsheet] Building acid-gas-removal flowsheet...");
            AcidGasRemovalDynamicTemplate.Generate(outputFile);
            Console.WriteLine("[GenerateFlowsheet] Flowsheet saved to: " + outputFile);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[GenerateFlowsheet] ERROR: " + ex);
            return 1;
        }
    }

    static string LocateExamplesDir()
    {
        string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                        ?? Directory.GetCurrentDirectory();

        // If scripts were copied next to the binary (CopyToOutputDirectory scenario).
        if (File.Exists(Path.Combine(asmDir, "integrator_pre_step_feed_profile.py")))
            return asmDir;

        // Walk upward from the binary dir looking for the examples subfolder.
        var dir = new DirectoryInfo(asmDir);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "examples", "acid-gas-removal-dynamics");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "integrator_pre_step_feed_profile.py")))
                return candidate;
            dir = dir.Parent;
        }

        throw new Exception(
            "Cannot locate 'examples/acid-gas-removal-dynamics' folder. " +
            "Ensure the solution root is an ancestor of the binary location.");
    }
}
