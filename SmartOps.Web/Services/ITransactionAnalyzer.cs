using System.Threading.Tasks;
using SmartOps.Core.Entities;

namespace SmartOps.Web.Services
{
    public interface ITransactionAnalyzer
    {
        Task<string> AnalyzeAsync(Transaction tx);
    }
}
