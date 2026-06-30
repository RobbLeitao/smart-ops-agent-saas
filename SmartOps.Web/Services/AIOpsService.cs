using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace SmartOps.Web.Services
{
    public interface IAIOpsService
    {
        Task<string> ExecutePromptAsync(string userPrompt);
    }

    public sealed class AIOpsService : IAIOpsService
    {
        private readonly Kernel _kernel;

        public AIOpsService(Kernel kernel)
        {
            _kernel = kernel ?? throw new System.ArgumentNullException(nameof(kernel));
        }

        public async Task<string> ExecutePromptAsync(string userPrompt)
        {
            if (userPrompt is null) throw new System.ArgumentNullException(nameof(userPrompt));

            var settings = new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var result = await _kernel.InvokePromptAsync(userPrompt, new(settings));

            return result.ToString();
        }
    }
}
