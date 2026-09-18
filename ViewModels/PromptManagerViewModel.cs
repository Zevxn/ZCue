using TypeSense.Models;
using TypeSense.Services;

namespace TypeSense.ViewModels;

public sealed class PromptManagerViewModel
{
    public PromptManagerViewModel(PromptCatalogService catalog)
    {
        Prompts = catalog.GetAllItems();
    }

    public IReadOnlyList<PromptItem> Prompts { get; }
}
