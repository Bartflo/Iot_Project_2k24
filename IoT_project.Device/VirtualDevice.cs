using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx.Client;
using System.Net.Mime;
using System.Text;

namespace IoT_project.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient client;
        private OpcClient OPC;
        public VirtualDevice(DeviceClient deviceClient, OpcClient OPC)
        {
            this.client = deviceClient;
            this.OPC = OPC;
        }
        #region D2C telemetry
        public async Task SendTelemetry(dynamic data)
        {
            var dataString = JsonConvert.SerializeObject(data);
            Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
            eventMessage.ContentType = MediaTypeNames.Application.Json;
            eventMessage.ContentEncoding = "utf-8";
            await client.SendEventAsync(eventMessage);
        }
        #endregion
    }
}
