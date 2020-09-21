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
    }
}
