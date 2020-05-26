using CefSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace cefsharptest.LivelyLib
{   
    /// <summary>
    /// {Incomplete}
    /// </summary>
    class FileWatcher
    {
        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public FileWatcher(string path)
        {
            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = path,

                // Watch for changes in LastAccess and LastWrite times, and
                // the renaming of files or directories.
                NotifyFilter = NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
            };

            // Only watch text files.
            //watcher.Filter = "*.txt";

            // Add event handlers.
            watcher.Changed += Watcher_Changed;
            watcher.Created += Watcher_Changed; 
            watcher.Deleted += Watcher_Changed;
            watcher.Renamed += Watcher_Renamed;

            // Begin watching.
            watcher.EnableRaisingEvents = true;
            //watcher.EndInit();
            //watcher.Dispose();
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.OldFullPath} renamed to {e.FullPath}");
            Form1.chromeBrowser.Reload();
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            // Specify what is done when a file is changed, created, or deleted.
            Console.WriteLine($"File: {e.FullPath} {e.ChangeType}");
            Form1.chromeBrowser.Reload();
        }
    }
}
