using System;
using System.Threading.Tasks;

namespace SmartOps.Web.Tests
{
    internal class ControllableFakeAIOps : SmartOps.Web.Services.IAIOpsService
    {
        private readonly TaskCompletionSource<string> _tcs;
        public ControllableFakeAIOps(TaskCompletionSource<string> tcs) => _tcs = tcs;

        public Task<string> ExecutePromptAsync(string userPrompt)
        {
            return _tcs.Task;
        }
    }
}
