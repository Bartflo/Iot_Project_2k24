using IoT_project.Device;
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
        string configFileJsonPath = Path.Combine(Directory.GetParent(AppContext.BaseDirectory).Parent.Parent.Parent.FullName, "config.json");

        string deviceConnectionString = "";
        string OPCserverURL = "";
        List<string> devicesNames = new List<string>();

        // Reading JSON configuration file
        try
        {
            string configJson = File.ReadAllText(configFileJsonPath);
            var configObject = JsonConvert.DeserializeObject<dynamic>(configJson);

            if (configObject.deviceConnectionString != null)
            {
                deviceConnectionString = configObject.deviceConnectionString.ToString();
            }
            else
            {
                Console.WriteLine("Error: 'deviceConnectionString' not found in the configuration file.");
                return; 
            }
            if(configObject.OPCserverURL != null)
            {
                OPCserverURL = configObject.OPCserverURL.ToString();
            }
            else
            {
                Console.WriteLine("Error: 'OPCserverURL' not found in the configuration file.");
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
        using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
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

            var device = new VirtualDevice(deviceClient, client);
            await device.InitializeHandlers();
            //await device.ClearReportedTwinAsync();
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
                await Task.Delay(12500); 
            }
            client.Disconnect();
        }
        Console.ReadLine();
    }
}
