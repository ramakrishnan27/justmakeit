using Platform.Content.Interfaces;

namespace Platform.Content.Features.AI;

/// <summary>No AI assistant is built or configured, so the page's AI panel stays switched off. Replaced when the AI Studio exists.</summary>
public sealed class NotConfiguredAiAssistant : IContentAiAssistant
{
    public bool IsAvailable => false;
}
