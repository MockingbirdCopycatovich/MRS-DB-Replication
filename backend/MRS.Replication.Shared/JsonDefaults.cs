using System.Text.Json;
using System.Text.Json.Serialization;

namespace MRS.Replication.Shared;

/// <summary>Shared JSON options: enums as strings (e.g. "Active" not "1") so the DTOs are readable across the wire and directly usable by the Angular frontend.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
