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
        public VirtualDevice(DeviceClient deviceClient)
        {
            this.client = deviceClient;
        }
    }
}
