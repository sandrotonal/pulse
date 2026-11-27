using System.Text.Json.Serialization;
using Pulse.Core.Models;

namespace Pulse.Services.Settings;

/// <summary>
/// Source-generated serializer metadata keeps settings working in trimmed Release builds.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
