namespace TeamWare.Web.Services;

public class AiAssistantService : IAiAssistantService
{
    private readonly IOllamaService _ollamaService;

    public AiAssistantService(IOllamaService ollamaService)
    {
        _ollamaService = ollamaService;
    }

    public Task<bool> IsAvailable()
    {
        return _ollamaService.IsConfigured();
    }

    public Task<ServiceResult<string>> RewriteProjectDescription(string description)
    {
        var systemPrompt = "You are a professional technical writer. Rewrite the following project description " +
            "to be clear, professional, and well-structured. Preserve the original meaning and all factual details. " +
            "Return only the rewritten description with no preamble, commentary, or explanation.";

        return _ollamaService.GenerateCompletion(systemPrompt, description);
    }

    public Task<ServiceResult<string>> RewriteTaskDescription(string description)
    {
        var systemPrompt = "You are a professional technical writer. Rewrite the following task description " +
            "as a clear, actionable work item. Preserve the original meaning and all requirements. " +
            "Return only the rewritten description with no preamble, commentary, or explanation.";

        return _ollamaService.GenerateCompletion(systemPrompt, description);
    }

    public Task<ServiceResult<string>> PolishComment(string comment)
    {
        var systemPrompt = "You are a professional editor. Polish the following comment for clarity and tone " +
            "while preserving the original intent. Keep the length similar to the original. " +
            "Return only the polished comment with no preamble, commentary, or explanation.";

        return _ollamaService.GenerateCompletion(systemPrompt, comment);
    }

    public Task<ServiceResult<string>> ExpandInboxItem(string title, string? description)
    {
        var systemPrompt = "You are a professional technical writer. Expand the following brief note into a " +
            "fuller description suitable for a task or work item. Add relevant detail and structure while " +
            "preserving the original intent. Return only the expanded description with no preamble, commentary, or explanation.";

        var userPrompt = string.IsNullOrWhiteSpace(description)
            ? title
            : $"{title}\n\n{description}";

        return _ollamaService.GenerateCompletion(systemPrompt, userPrompt);
    }

    public Task<ServiceResult<string>> GenerateProjectSummary(int projectId, string userId, SummaryPeriod period)
    {
        // Stubbed — implemented in Phase 24
        return Task.FromResult(ServiceResult<string>.Failure("Summary generation is not yet implemented."));
    }

    public Task<ServiceResult<string>> GeneratePersonalDigest(string userId)
    {
        // Stubbed — implemented in Phase 24
        return Task.FromResult(ServiceResult<string>.Failure("Summary generation is not yet implemented."));
    }

    public Task<ServiceResult<string>> GenerateReviewPreparation(string userId)
    {
        // Stubbed — implemented in Phase 24
        return Task.FromResult(ServiceResult<string>.Failure("Summary generation is not yet implemented."));
    }
}
