using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace livelywpf.Core.API
{
    class IpcMessageConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(IpcMessage));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            switch ((MessageType)jo["Type"].Value<int>())
            {
                case MessageType.cmd_reload:
                    return jo.ToObject<LivelyReloadCmd>(serializer);
                case MessageType.cmd_close:
                    return jo.ToObject<LivelyCloseCmd>(serializer);
                case MessageType.cmd_screenshot:
                    return jo.ToObject<LivelyScreenshotCmd>(serializer);
                case MessageType.lsp_perfcntr:
                    return jo.ToObject<LivelySystemInformation>(serializer);
                case MessageType.lsp_nowplaying:
                    return jo.ToObject<LivelySystemNowPlaying>(serializer);
                case MessageType.lp_slider:
                    return jo.ToObject<LivelySlider>(serializer);
                case MessageType.lp_textbox:
                    return jo.ToObject<LivelyTextBox>(serializer);
                case MessageType.lp_dropdown:
                    return jo.ToObject<LivelyDropdown>(serializer);
                case MessageType.lp_fdropdown:
                    return jo.ToObject<LivelyFolderDropdown>(serializer);
                case MessageType.lp_button:
                    return jo.ToObject<LivelyButton>(serializer);
                case MessageType.lp_cpicker:
                    return jo.ToObject<LivelyColorPicker>(serializer);
                case MessageType.lp_chekbox:
                    return jo.ToObject<LivelyCheckbox>(serializer);
                case MessageType.msg_console:
                    return jo.ToObject<LivelyMessageConsole>(serializer);
                case MessageType.msg_hwnd:
                    return jo.ToObject<LivelyMessageHwnd>(serializer);
                case MessageType.msg_screenshot:
                    return jo.ToObject<LivelyMessageScreenshot>(serializer);
                case MessageType.msg_wploaded:
                    return jo.ToObject<LivelyMessageWallpaperLoaded>(serializer);
                default:
                    return null;
            }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
