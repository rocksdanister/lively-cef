using Newtonsoft.Json.Linq;
using System.IO;

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
