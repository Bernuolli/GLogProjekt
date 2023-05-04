namespace GriffSoft.GLog.WorkerService.Logic.Services.Contracts
{
    public interface IGlogAlertFactory
    {
        Task CreateAlerts(TimeSpan timeSpanUntilNextRun, TimeSpan alertRunTimeoutInMinutes);
    }
}
