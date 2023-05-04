namespace GriffSoft.GLog.WorkerService.Logic.Services.Contracts;

public interface IGLogCleanerService
{
    /// <summary>
    /// Törli azokat a glog adatokat, amik <paramref name="logStorageDurationInDays"/> napnál régebbiek, majd ha van olyan GLogMasterData, amihez nem tartozik
    /// glog, akkor azt is.
    /// </summary>
    /// <param name="logStorageDurationInDays">Mennyi napig visszamenőleg maradjanak meg a log bejegyzések.</param>
    /// <returns></returns>
    Task ClearLogData( int logStorageDurationInDays );
}