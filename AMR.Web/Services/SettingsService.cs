using System.Text.Json;
using System.Text.Json.Nodes;
using AMR.Web.Models;

namespace AMR.Web.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private const string MqttSectionName = "MqttSettings";
    private const string ModbusSectionName = "ModbusSettings";
    private const string CobotModbusSectionName = "CobotModbusSettings";
    private const string IoModuleModbusSectionName = "IoModuleModbusSettings";

    public SettingsService(IWebHostEnvironment env)
    {
        _settingsFilePath = Path.Combine(env.ContentRootPath, "appsettings.json");
    }

    public MqttSettings LoadMqtt()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[MqttSectionName];

        if (section is null)
            return new MqttSettings();

        return new MqttSettings
        {
            BrokerAddress = section["BrokerAddress"]?.GetValue<string>() ?? "localhost",
            BrokerPort = section["BrokerPort"]?.GetValue<int>() ?? 1883,
            ClientId = section["ClientId"]?.GetValue<string>() ?? "AMR-Client",
            Username = section["Username"]?.GetValue<string>(),
            Password = section["Password"]?.GetValue<string>()
        };
    }

    public ModbusSettings LoadModbus()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[ModbusSectionName];

        if (section is null)
            return new ModbusSettings();

        return new ModbusSettings
        {
            IpAddress = section["IpAddress"]?.GetValue<string>() ?? "127.0.0.1",
            Port = section["Port"]?.GetValue<int>() ?? 5020,
            SlaveId = (byte)(section["SlaveId"]?.GetValue<int>() ?? 1)
        };
    }

    public CobotModbusSettings LoadCobotModbus()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[CobotModbusSectionName];

        if (section is null)
            return new CobotModbusSettings();

        return new CobotModbusSettings
        {
            IpAddress = section["IpAddress"]?.GetValue<string>() ?? "127.0.0.1",
            Port = section["Port"]?.GetValue<int>() ?? 502,
            SlaveId = (byte)(section["SlaveId"]?.GetValue<int>() ?? 1)
        };
    }

    public IoModuleModbusSettings LoadIoModuleModbus()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[IoModuleModbusSectionName];

        if (section is null)
            return new IoModuleModbusSettings();

        return new IoModuleModbusSettings
        {
            IpAddress = section["IpAddress"]?.GetValue<string>() ?? "127.0.0.1",
            Port = section["Port"]?.GetValue<int>() ?? 502,
            SlaveId = (byte)(section["SlaveId"]?.GetValue<int>() ?? 1)
        };
    }

    public void Save(MqttSettings mqttSettings, ModbusSettings modbusSettings,
        CobotModbusSettings? cobotModbusSettings = null,
        IoModuleModbusSettings? ioModuleModbusSettings = null)
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json)!;

        doc[MqttSectionName] = JsonSerializer.SerializeToNode(mqttSettings);
        doc[ModbusSectionName] = JsonSerializer.SerializeToNode(modbusSettings);

        if (cobotModbusSettings != null)
            doc[CobotModbusSectionName] = JsonSerializer.SerializeToNode(cobotModbusSettings);

        if (ioModuleModbusSettings != null)
            doc[IoModuleModbusSectionName] = JsonSerializer.SerializeToNode(ioModuleModbusSettings);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_settingsFilePath, doc.ToJsonString(options));
    }
}
