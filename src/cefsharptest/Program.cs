using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace cefsharptest
{

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                bool onlyInstance = false;
                Mutex mutex = new Mutex(true, "LIVELY:DESKTOPWALLPAPERSYSTEM", out onlyInstance);
                if (!onlyInstance)
                {

                }
                else
                {
                    MessageBox.Show("Lively is not running, Exiting!", "Cef: Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1); //msgloop not ready 
                }

            }
            catch (AbandonedMutexException e)
            {
                //Note to self:- logger backup(in the even of previous lively crash) is at App() contructor func, DO NOT start writing loghere to avoid overwriting crashlog.
                System.Diagnostics.Debug.WriteLine(e.Message);
            }

            /*
            //Simple check.
            bool cont = false;
            foreach (var item in Process.GetProcesses())
            {
                if (String.Equals(item.ProcessName, "livelywpf", StringComparison.OrdinalIgnoreCase))
                {
                    cont = true;
                    break;
                }
            }
            if(cont == false)
            {
                MessageBox.Show("Lively is not running, Exiting!", "Cef: Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1); //msgloop not ready 
            }
            */

            //deleting old CEF logfile.
            try
            {
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"));
            }
            catch { }

            //cscore is culture sensitive?, fix?
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

    }
}
