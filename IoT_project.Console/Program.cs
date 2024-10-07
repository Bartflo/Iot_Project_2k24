using Azure.Messaging.ServiceBus;
using IoT_project.Device;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;
using Opc.UaFx;
using Opc.UaFx.Client;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Started");
        // Path to configuration file
        string configFileJsonPath = Path.Combine(AppContext.BaseDirectory, "config.json");

        string deviceConnectionString = "";
        string OPCserverURL = "";
        string serviceBusConnectionString = "";
        string registryManagerConnectionString = "";
        string emergencyStopQueue = "";
        string decreaseProductionRateQueue = "";
        string azureDeviceName = "";
        List<string> devicesNames = new List<string>();

        // Reading JSON configuration file
        try
        {
            string configJson = File.ReadAllText(configFileJsonPath);
            var configObject = JsonConvert.DeserializeObject<dynamic>(configJson);

            if (configObject.deviceConnectionString != null)
            {
                deviceConnectionString = configObject.deviceConnectionString.ToString();
            }else
            {
                Console.WriteLine("Error: 'deviceConnectionString' not found in the configuration file.");
                return; 
            }
            if(configObject.OPCserverURL != null)
            {
                OPCserverURL = configObject.OPCserverURL.ToString();
            }else
            {
                Console.WriteLine("Error: 'OPCserverURL' not found in the configuration file.");
                return;
            }
            if (configObject.serviceBusConnectionString != null)
            {
                serviceBusConnectionString = configObject.serviceBusConnectionString.ToString();
            }else
            {
                Console.WriteLine("Error: 'serviceBusConnectionString' not found in the configuration file.");
                return;
            }
            if (configObject.registryManagerConnectionString != null)
            {
                registryManagerConnectionString = configObject.registryManagerConnectionString.ToString();
            }else
            {
                Console.WriteLine("Error: 'registryManagerConnectionString' not found in the configuration file.");
                return;
            }
            if (configObject.emergencyStopQueue != null)
            {
                emergencyStopQueue = configObject.emergencyStopQueue.ToString();
            }else
            {
                Console.WriteLine("Error: 'emergencyStopQueue' not found in the configuration file.");
                return;
            }
            if(configObject.decreaseProductionRateQueue != null)
            {
                decreaseProductionRateQueue = configObject.decreaseProductionRateQueue.ToString();
            }else
            {
                Console.WriteLine("Error: 'decreaseProductionRateQueue' not found in the configuration file.");
                return;
            }
            if(configObject.azureDeviceName != null)
            {
                azureDeviceName = configObject.azureDeviceName.ToString();
            }else
            {
                Console.WriteLine("Error: 'azureDeviceName' not found in the configuration file.");
                return;
            }

            Console.WriteLine("Configuration file read successfully");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to read configuration file: {e.Message}");
            return;
        }

        if (string.IsNullOrEmpty(deviceConnectionString))
        {
            Console.WriteLine("Device connection string is empty. Exiting...");
            return;
        }
        // Start the connection
        using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
        await deviceClient.OpenAsync();
        Console.WriteLine("Connection to Azure success");

        using(var client = new OpcClient(OPCserverURL))
        {
            client.Connect();
            Console.WriteLine("Connection to OPC UA success");
            devicesNames = client.BrowseNode(OpcObjectTypes.ObjectsFolder)
                .Children()
                .Where(node => node.DisplayName.Value.Contains("Device"))
                .Select(node => node.DisplayName.Value)
                .ToList();

            using var registryManager = RegistryManager.CreateFromConnectionString(registryManagerConnectionString);
            var device = new VirtualDevice(deviceClient, client, registryManager, azureDeviceName);
            await device.InitializeHandlers();
            //await device.ClearReportedTwinAsync();

            await using ServiceBusClient serviceBus_client = new ServiceBusClient(serviceBusConnectionString);
            await using ServiceBusProcessor emergencyStopQueue_processor = serviceBus_client.CreateProcessor(emergencyStopQueue);
            await using ServiceBusProcessor decreaseProductionRateQueue_processor = serviceBus_client.CreateProcessor(decreaseProductionRateQueue);

            emergencyStopQueue_processor.ProcessMessageAsync += device.EmergencyStop_ProcessMessageAsync;
            emergencyStopQueue_processor.ProcessErrorAsync += device.Message_ProcessorError;

            decreaseProductionRateQueue_processor.ProcessMessageAsync += device.DecreaseProductionRate_ProcessMessageAsync;
            decreaseProductionRateQueue_processor.ProcessErrorAsync += device.Message_ProcessorError;

            await emergencyStopQueue_processor.StartProcessingAsync();
            await decreaseProductionRateQueue_processor.StartProcessingAsync();
            while (devicesNames.Count > 0)
            {
                Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                foreach (string deviceName in devicesNames)
                {
                    string baseNode = $"ns=2;s={deviceName}/";
                    var nodeNames = new[]
                    {
                        "ProductionStatus",
                        "ProductionRate",
                        "WorkorderId",
                        "Temperature",
                        "GoodCount",
                        "BadCount",
                        "DeviceError"
                    };

                    List<OpcValue> nodeValues = new List<OpcValue>();
                    foreach (var nodeName in nodeNames)
                    {
                        OpcValue value = client.ReadNode(baseNode + nodeName);
                        nodeValues.Add(value);
                    }

                    var telemetryData = new TelemetryData
                    {
                        DeviceName = deviceName,
                        ProductionStatus = nodeValues[0].Value,
                        ProductionRate = nodeValues[1].Value,
                        WorkorderId = nodeValues[2].Value,
                        Temperature = nodeValues[3].Value,
                        GoodCount = nodeValues[4].Value,
                        BadCount = nodeValues[5].Value,
                        DeviceErrors = nodeValues[6].Value
                    };

                    await device.SendTelemetry(telemetryData);

                }
                await Task.Delay(10000); 
            }
            client.Disconnect();
        }
        Console.ReadLine();
    }
}
