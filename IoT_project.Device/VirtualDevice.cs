using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.UaFx;
using Opc.UaFx.Client;
using System.Diagnostics;
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
        public async Task SendTelemetry(TelemetryData telemetryData)
        {
            var twin = await client.GetTwinAsync();
            var reportedProperties = twin.Properties.Reported;
            var nameDevice = telemetryData.DeviceName.Replace(" ", "");
            var deviceErrorKey = $"{nameDevice}_error_code";
            var errorStatus = telemetryData.DeviceErrors;

            var isErrorChanged = reportedProperties.Contains(deviceErrorKey) && reportedProperties[deviceErrorKey]?.ToString() == errorStatus?.ToString();

            string selectedDataString;
            if (isErrorChanged)
            {
                var data = new
                {
                    telemetryData.DeviceName,
                    telemetryData.ProductionStatus,
                    telemetryData.WorkorderId,
                    telemetryData.GoodCount,
                    telemetryData.BadCount,
                    telemetryData.Temperature
                };
                selectedDataString = JsonConvert.SerializeObject(data);
                Console.WriteLine(JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            else
            {
                var data = new
                {
                    telemetryData.DeviceName,
                    telemetryData.ProductionStatus,
                    telemetryData.WorkorderId,
                    telemetryData.Temperature,
                    telemetryData.GoodCount,
                    telemetryData.BadCount,
                    telemetryData.DeviceErrors
                };
                selectedDataString = JsonConvert.SerializeObject(data);
                Console.WriteLine(JsonConvert.SerializeObject(data, Formatting.Indented));
            }

            var eventMessage = new Message(Encoding.UTF8.GetBytes(selectedDataString))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = "utf-8"
            };

            await client.SendEventAsync(eventMessage);

            await UpdateTwinAsync(nameDevice, errorStatus, telemetryData.ProductionRate);
        }

        #endregion

        #region Device Twin
        public async Task ClearReportedTwinAsync()
        {
            var twin = await client.GetTwinAsync();

            var reportedProperties = new TwinCollection();
            string reportedPropertiesJSON = twin.Properties.Reported.ToJson(Formatting.None);
            Dictionary<string, object> propertiesDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(reportedPropertiesJSON);
            propertiesDict.Remove("$version");
            foreach (var property in propertiesDict )
            {
                    reportedProperties[property.Key] = null;
            }

            try
            {
                await client.UpdateReportedPropertiesAsync(reportedProperties);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing reported properties: {ex.Message}\nReported Properties: {twin.Properties.Reported.ToJson(Formatting.None)}");
            }
        }

        public async Task UpdateTwinAsync(string deviceName, object deviceError, object prodRate)
        {
            var twin = await client.GetTwinAsync();
            var reportedProp = twin.Properties.Reported;

            var name = deviceName.Replace(" ", "");
            var device_error = $"{name}_error_code";
            var device_production = $"{name}_production_rate";

            var device_error_code = deviceError?.ToString(); 
            var device_production_rate = prodRate?.ToString();

            if (reportedProp.Contains(device_error))
            {
                var current_error = reportedProp[device_error]?.ToString();
                if (current_error != device_error_code)
                {
                    await UpdateReportedProperty(device_error, device_error_code);
                }
            }
            else
            {
                await UpdateReportedProperty(device_error, device_error_code);
            }

            if (reportedProp.Contains(device_production))
            {
                var productionInTgisMoment = reportedProp[device_production]?.ToString();
                if (productionInTgisMoment != device_production_rate)
                {
                    await UpdateReportedProperty(device_production, device_production_rate);
                }
            }
            else
            {
                await UpdateReportedProperty(device_production, device_production_rate);
            }
        }

        private async Task UpdateReportedProperty(string propertyName, string propertyValue)
        {
            var updateProp = new TwinCollection();
            updateProp[propertyName] = propertyValue;

            try
            {
                await client.UpdateReportedPropertiesAsync(updateProp);
                Console.WriteLine($"Property value changed: {propertyName}.");
            }
            catch (IotHubException ex)
            {
                Console.WriteLine($"Error changing value: {propertyName}. Exception: {ex.Message}");
            }
        }

        #endregion
        #region Direct Methods
        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");
            await Task.Delay(1000);
            return new MethodResponse(0);
        }
        private async Task<MethodResponse> EmergencyStop(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { deviceName = default(string) });
            Console.WriteLine($"\t Emergency stop executed for: {payload.deviceName}");
            OPC.CallMethod($"ns=2;s={payload.deviceName}",$"ns=2;s={payload.deviceName}/EmergencyStop");
            return new MethodResponse(0);
        }
        private async Task<MethodResponse> ResetErrorStatus(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { deviceName = default(string) });
            Console.WriteLine($"\t Reset error status executed for: {payload.deviceName}");
            OPC.CallMethod($"ns=2;s={payload.deviceName}", $"ns=2;s={payload.deviceName}/ResetErrorStatus");
            return new MethodResponse(0);
        }
        #endregion
        public async Task InitializeHandlers()
        {
            await client.SetMethodHandlerAsync("EmergencyStop", EmergencyStop, client);
            await client.SetMethodHandlerAsync("ResetErrorStatus",ResetErrorStatus, client);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, client);
        }
    }
    public class TelemetryData
    {
        public string DeviceName { get; set; }
        public object ProductionStatus { get; set; }
        public object ProductionRate { get; set; }
        public object WorkorderId { get; set; }
        public object Temperature { get; set; }
        public object GoodCount { get; set; }
        public object BadCount { get; set; }
        public object DeviceErrors { get; set; }
    }
}
