using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.UaFx;
using Opc.UaFx.Client;
using System.Diagnostics;
using System.Net.Mime;
using System.Text;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace IoT_project.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient client;
        private OpcClient OPC;
        private RegistryManager registryManager;
        private string azureDeviceName;

        public VirtualDevice(DeviceClient deviceClient, OpcClient OPC, RegistryManager registryManager, string azureDeviceName)
        {
            this.client = deviceClient;
            this.OPC = OPC;
            this.registryManager = registryManager;
            this.azureDeviceName = azureDeviceName;
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

            await UpdateTwinAsync(telemetryData.DeviceName, errorStatus, telemetryData.ProductionRate);
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
            var desiredProp = twin.Properties.Desired;
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
                var currentProductionRate = reportedProp[device_production]?.ToString();
                if (currentProductionRate != device_production_rate)
                {
                    await UpdateReportedProperty(device_production, device_production_rate);
                }
            }
            else
            {
                await UpdateReportedProperty(device_production, device_production_rate);
            }

            var device_production_desired = $"{name}_production_rate";
            if(desiredProp.Contains(device_production_desired))
            {
                var desiredProductionRate = desiredProp[device_production_desired]?.ToString();
                if (desiredProductionRate != null && desiredProductionRate != device_production_rate)
                {
                    int int_desiredProductionRate;
                    int.TryParse(desiredProductionRate, out int_desiredProductionRate);
                    OPC.WriteNode($"ns=2;s={deviceName}/ProductionRate", int_desiredProductionRate);
                }
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
        #region Business logic
        public async Task EmergencyStop_ProcessMessageAsync(ProcessMessageEventArgs arg)
        {
            string messageBody = Encoding.UTF8.GetString(arg.Message.Body.ToArray());
            var messageContent = JsonConvert.DeserializeObject<dynamic>(messageBody);
            string deviceName = messageContent.deviceName;

            string payload = "{\"deviceName\":\"" + deviceName + "\"}";
            byte[] byte_payload = Encoding.ASCII.GetBytes(payload);
            MethodRequest methodRequest = new MethodRequest(JsonConvert.SerializeObject(payload), byte_payload);

            await EmergencyStop(methodRequest, client);
            await arg.CompleteMessageAsync(arg.Message);
        }
        public async Task DecreaseProductionRate_ProcessMessageAsync(ProcessMessageEventArgs arg)
        {
            string messageBody = Encoding.UTF8.GetString(arg.Message.Body.ToArray());
            var messageContent = JsonConvert.DeserializeObject<dynamic>(messageBody);
            string deviceName = messageContent.deviceName;
            string deviceProperty = $"{deviceName.Replace(" ", "")}_production_rate";

            var twin = await registryManager.GetTwinAsync(azureDeviceName);
            var reportedProp = twin.Properties.Reported;
            var desiredProp = twin.Properties.Desired;

            Console.WriteLine("decrease");

            if (desiredProp.Contains(deviceProperty) && reportedProp.Contains(deviceProperty))
            {
                if (int.TryParse((string)reportedProp[deviceProperty], out int currentRate))
                {
                    int newRate = Math.Max(currentRate - 10, 0);

                    if (currentRate != newRate)
                    {
                        desiredProp[deviceProperty] = newRate;
                        await registryManager.UpdateTwinAsync(twin.DeviceId, twin, twin.ETag);
                        Console.WriteLine($"Production Rate decreased for: {deviceName} by 10%");
                    }
                }
            }
            await arg.CompleteMessageAsync(arg.Message);
        }


        public Task Message_ProcessorError(ProcessErrorEventArgs arg)
        {
            Console.WriteLine($"Service bus error: {arg.Exception.Message}");
            return Task.CompletedTask;
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
