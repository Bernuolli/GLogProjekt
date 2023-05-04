using GriffSoft.GLog.DataAccess;
using GriffSoft.GLog.DataAccess.LogDb;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.Shared.Forras.Contracts.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GriffSoft.GLog.WorkerService.Logic.Services;

public class GLogCleanerService : IGLogCleanerService
{
    private readonly ILogger<GLogCleanerService> _logger;
    private readonly IGriffDbContextFactory<GLogContext> _gLogContextFactory;

    private static int _deleteBatchSize = 20_000;
    private static int _deleteIterationThreshold = 60;

    public GLogCleanerService( ILogger<GLogCleanerService> logger, IGriffDbContextFactory<GLogContext> gLogContextFactory)
    {
        _logger = logger;
        _gLogContextFactory = gLogContextFactory;
    }

    /// <summary>
    /// Törli azokat a glog adatokat, amik <paramref name="logStorageDurationInDays"/> napnál régebbiek, majd ha van olyan GLogMasterData, amihez nem tartozik
    /// glog, akkor azokat is maximum 50000 rekordig.
    /// </summary>
    /// <param name="logStorageDurationInDays">Mennyi napig visszamenőleg maradjanak meg a log bejegyzések.</param>
    /// <returns></returns>
    public async Task ClearLogData(int logStorageDurationInDays )
    {
        //NOTE: a GLog-hoz tartozó GLogHost és GLogConfig szándékosan nem kerülnek törlésre, egyelőre nincsenek használatban.
        await using var context = await _gLogContextFactory.CreateDbContextAsync();

        DateTime dateTimeToKeepUntil = GetDateTimeToKeepUntil( logStorageDurationInDays );
        int currentIteration = 0;
        bool noMoreToRemove = false;
        int numRowsDeleted = 0;
        while( currentIteration < _deleteIterationThreshold && !noMoreToRemove )
        {
            var logsToRemove = context.GLog.Where( glog => glog.CreatedOn < dateTimeToKeepUntil ).Take(_deleteBatchSize);
            if( !logsToRemove.Any() )
            {
                noMoreToRemove = true;
                continue;
            }
            context.RemoveRange( logsToRemove );
            numRowsDeleted += logsToRemove.Count();
            await Task.Delay( 5 );
            ++currentIteration;
        }

        int numRowsDeletedMasterData = await context.Database.ExecuteSqlRawAsync( @"
                DELETE TOP (50000) FROM GLogMasterData
                WHERE NOT EXISTS (SELECT TOP 1 1 FROM GLog gl WHERE gl.MasterDataId = GLogMasterData.Id)" );

        _logger.LogDebugWithLevelCheck( $"{nameof( ClearLogData )}-> number of GLog rows deleted:{numRowsDeleted}" );
        _logger.LogDebugWithLevelCheck( $"{nameof( ClearLogData )}-> number of MasterData rows deleted:{numRowsDeletedMasterData}" );
    }

    private static DateTime GetDateTimeToKeepUntil( int logStorageDurationInDays )
    {
        var now = DateTime.Now;
        DateTime curHour = new DateTime( now.Year, now.Month, now.Day, now.Hour, 0, 0 );
        var timeToKeepUntil = curHour.AddDays( -logStorageDurationInDays );
        return timeToKeepUntil;
    }
}