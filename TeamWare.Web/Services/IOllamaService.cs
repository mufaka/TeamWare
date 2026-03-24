namespace TeamWare.Web.Services;

public interface IOllamaService
{
    Task<ServiceResult<string>> GenerateCompletion(string systemPrompt, string userPrompt);

    Task<bool> IsConfigured();
}
