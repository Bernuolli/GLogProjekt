using GriffSoft.GLog.WorkerService.Logic.Forras.Types;

namespace GriffSoft.GLog.WorkerService.Logic.Services.Contracts
{
    public interface IGLogLoggedDataRemoverService
    {
        Task RemoveLoggedDataFromList( List<RemoveLoggedDataParam> loggedData);
    }
}
