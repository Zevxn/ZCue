using System.Text.Json.Serialization;

namespace ZCue.Models;

public sealed class PromptCategory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    public PromptCategory Clone() => new()
    {
        Id = Id,
        Name = Name
    };
}
