using System.Text.Json.Serialization;
using Kleaner.Executor;

namespace Kleaner.WebHost;

/// <summary>
/// Web API 请求体的裁剪安全 JSON 元数据。
/// 最小 API 的请求绑定会在运行时反射 DTO；发布裁剪时必须显式保留这些构造器和属性。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HostSettings))]
[JsonSerializable(typeof(LargeFilesRequest))]
[JsonSerializable(typeof(DuplicatesRequest))]
[JsonSerializable(typeof(UsageRequest))]
[JsonSerializable(typeof(PlanRequest))]
[JsonSerializable(typeof(ConfirmRequest))]
[JsonSerializable(typeof(StartupIdRequest))]
[JsonSerializable(typeof(JobAcceptedView))]
[JsonSerializable(typeof(PlanView))]
[JsonSerializable(typeof(ExecutionReport))]
internal partial class KleanerJsonContext : JsonSerializerContext;

/// <summary>settings.json 的兼容上下文：磁盘字段名沿用既有 PascalCase 契约。</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HostSettings))]
internal partial class SettingsFileJsonContext : JsonSerializerContext;
