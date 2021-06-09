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
                default:
                    return null;
            }
            /*
            return (MessageType)jo["Type"].Value<int>() switch
            {
                MessageType.cmd_reload => jo.ToObject<LivelyReloadCmd>(serializer),
                MessageType.cmd_close => jo.ToObject<LivelyCloseCmd>(serializer),
                MessageType.lsp_perfcntr => jo.ToObject<LivelySystemInformation>(serializer),
                MessageType.lsp_nowplaying => jo.ToObject<LivelySystemNowPlaying>(serializer),
                MessageType.lp_slider => jo.ToObject<LivelySlider>(serializer),
                MessageType.lp_textbox => jo.ToObject<LivelyTextBox>(serializer),
                MessageType.lp_dropdown => jo.ToObject<LivelyDropdown>(serializer),
                MessageType.lp_fdropdown => jo.ToObject<LivelyFolderDropdown>(serializer),
                MessageType.lp_button => jo.ToObject<LivelyButton>(serializer),
                MessageType.lp_cpicker => jo.ToObject<LivelyColorPicker>(serializer),
                MessageType.lp_chekbox => jo.ToObject<LivelyCheckbox>(serializer),
                _ => null,
             };
            */
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
