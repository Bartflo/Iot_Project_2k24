using IoT_project.Device;
using Microsoft.Azure.Devices.Client;
using Newtonsoft.Json;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Started");
        // Path to configuration file
        string configFileJsonPath = Path.Combine(Directory.GetParent(AppContext.BaseDirectory).Parent.Parent.Parent.FullName, "config.json");

        string deviceConnectionString = "";
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

            if (configObject.devices != null)
            {
                foreach (var deviceName in configObject.devices)
                {
                    devicesNames.Add(deviceName.ToString());
                }
            }
            else
            {
                Console.WriteLine("Error: 'devices' not found in the configuration file.");
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

        if (devicesNames.Count == 0)
        {
            Console.WriteLine("Devices list is empty. Exiting...");
            return;
        }

        // Start the connection
        using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
        await deviceClient.OpenAsync();
        var device = new VirtualDevice(deviceClient);
        Console.WriteLine("Connection success");
        Console.ReadLine();
    }
}
