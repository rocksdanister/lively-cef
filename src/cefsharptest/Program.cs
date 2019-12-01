using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

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
            //Simple check.
            bool cont = false;
            foreach (var item in Process.GetProcesses())
            {
                if (String.Equals(item.ProcessName, "livelywpf", StringComparison.InvariantCultureIgnoreCase))
                {
                    cont = true;
                    break;
                }
            }
            if(cont == false)
            {
                MessageBox.Show("Lively is not running, Exiting!", "Cef: Error");
                Environment.Exit(1); //msgloop not ready 
            }

            //deleting old CEF logfile.
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + "\\debug.log");
            if (File.Exists(logPath))
            {
                try
                {
                    File.Delete(logPath);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.ToString());
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

    }
}
