using Newtonsoft.Json.Linq;
using System.IO;

namespace Lively.PlayerCefSharp.Helpers
{
    public static class JsonUtil
    {
        public static void Write(string path, JObject rss)
        {
            File.WriteAllText(path, rss.ToString());
        }

        public static JObject Read(string path)
        {
            return JObject.Parse(File.ReadAllText(path));
        }
    }
}
