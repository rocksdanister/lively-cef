using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cefsharptest.Widgets
{
    public static class WidgetData
    {
        public static JObject liveyPropertiesData = null;
        public static void SaveLivelyProperties(string path, JObject rss)
        {
            File.WriteAllText(path, rss.ToString());
        }

        public static void LoadLivelyProperties(string path)
        {
            var json = File.ReadAllText(path);
            liveyPropertiesData = JObject.Parse(json);
        }

        [Serializable]
        public class FolderDropdownClass
        {
            public string FileName { get; set; }
            public string FullPath { get; set; }
            public string RelativePath { get; set; }
            public object MenuItem { get; set; }

            public FolderDropdownClass(string fileName, string fullPath, string relativePath, object obj)
            {
                FileName = fileName;
                FullPath = fullPath;
                RelativePath = relativePath;
                MenuItem = obj;
            }
        }
    }
}
