using System.Text.Json.Serialization;

namespace Md2Pdf.Core;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConversionResult))]
[JsonSerializable(typeof(DoctorResult))]
[JsonSerializable(typeof(BrowserManagementResult))]
[JsonSerializable(typeof(ExplorerManagementResult))]
[JsonSerializable(typeof(BrowserManifest))]
public partial class Md2PdfJsonContext : JsonSerializerContext;
