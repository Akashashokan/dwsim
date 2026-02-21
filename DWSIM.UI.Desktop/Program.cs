using System;
using System.IO;
using DWSIM.UI.Controls;
using Eto.Forms;
using DWSIM.GlobalSettings;
using System.Reflection;

namespace DWSIM.UI.Desktop
{
    public class Program
    {

        [STAThread]
        public static void Main(string[] args)
        {
            MainApp(args);
        }

        [STAThread]
        public static Application MainApp(string[] args)
        {

            // sets the assembly resolver to find remaining DWSIM libraries on demand

            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyResolve += new ResolveEventHandler(LoadFromNestedFolder);

            //initialize OpenTK

            OpenTK.Toolkit.Init();

            // set global settings

            Settings.CultureInfo = "en";
            Settings.EnableGPUProcessing = false;
            Settings.OldUI = false;

            Exception loadsetex = null;

            try
            {
                Settings.LoadSettings("dwsim_newui.ini");
            }
            catch (Exception ex)
            {
                loadsetex = ex;
            }

            Eto.Platform platform = null;

            try
            {
                switch (GlobalSettings.Settings.WindowsRenderer)
                {
                    case Settings.WindowsPlatformRenderer.WPF:
                        DWSIM.UI.Desktop.WPF.StyleSetter.SetTheme("aero", "normalcolor");
                        DWSIM.UI.Desktop.WPF.StyleSetter.SetStyles();
                        platform = new Eto.Wpf.Platform();
                        platform.Add<FlowsheetSurfaceControl.IFlowsheetSurface>(() => new WPF.FlowsheetSurfaceControlHandler());
                        platform.Add<Eto.OxyPlot.Plot.IHandler>(() => new Eto.OxyPlot.WPF2.PlotHandler());
                        platform.Add<Eto.Forms.Controls.Scintilla.Shared.ScintillaControl.IScintillaControl>(() => new Eto.Forms.Controls.Scintilla.WPF.ScintillaControlHandler());
                        break;
                    case Settings.WindowsPlatformRenderer.WinForms:
                    default:
                        DWSIM.UI.Desktop.WinForms.StyleSetter.SetStyles();
                        platform = new Eto.WinForms.Platform();
                        platform.Add<FlowsheetSurfaceControl.IFlowsheetSurface>(() => new WinForms.FlowsheetSurfaceControlHandler());
                        platform.Add<Eto.OxyPlot.Plot.IHandler>(() => new Eto.OxyPlot.WinForms.PlotHandler());
                        platform.Add<Eto.Forms.Controls.Scintilla.Shared.ScintillaControl.IScintillaControl>(() => new Eto.Forms.Controls.Scintilla.WinForms.ScintillaControlHandler());
                        break;
                }

                if (!GlobalSettings.Settings.AutomationMode)
                {
                    new Application(platform).Run(new MainForm());
                }
                else
                {
                    return new Application(platform);
                }
            }
            catch (Exception ex)
            {
                Logging.Logger.LogError("CPUI Error", ex);
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("APP CRASH!!!");
                Console.WriteLine();
                Console.WriteLine(ex.ToString());
                string configfiledir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DWSIM Application Data");
                if (!Directory.Exists(configfiledir)) Directory.CreateDirectory(configfiledir);
                if (ex.InnerException != null)
                {
                    File.WriteAllText(System.IO.Path.Combine(configfiledir, "lasterror.txt"), ex.InnerException.ToString());
                }
                else
                {
                    File.WriteAllText(System.IO.Path.Combine(configfiledir, "lasterror.txt"), ex.ToString());
                }
            }
            return null;
        }

        static Assembly LoadFromNestedFolder(object sender, ResolveEventArgs args)
        {
            string assemblyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "unitops", "libraries", new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(assemblyPath))
            {
                string assemblyPath2 = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "ppacks", "libraries", new AssemblyName(args.Name).Name + ".dll");
                if (!File.Exists(assemblyPath2))
                {
                    return null;
                }
                else
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyPath2);
                    return assembly;
                }
            }
            else
            {
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                return assembly;
            }
        }


    }

}
