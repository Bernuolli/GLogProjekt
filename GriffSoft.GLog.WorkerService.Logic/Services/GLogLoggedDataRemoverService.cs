using GriffSoft.Forras.DataAccess.FUV;
using GriffSoft.Forras.DataAccess.FUV.Contracts;
using GriffSoft.Forras.DataAccess.Install;
using GriffSoft.GLog.DataAccess.LogDb;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Logic.Services.Contracts;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.Shared.Forras.Contracts.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GriffSoft.GLog.WorkerService.Logic.Services
{
    public class GLogLoggedDataRemoverService : IGLogLoggedDataRemoverService
    {
        private readonly ILogger<GLogCleanerService> _logger;
        private readonly IDbContextFactory<InstallDbContext> _installContextFactory;
        private readonly IFUVGLogHelper _fuvHelper;
        private readonly string FORRASEVENTLOGTABLENAME = "ForrasEventLog";
        private readonly string FORRASAPPCALLSLOGTABLENAME = "ForrasAppCallsLog";
        private readonly string FORRASFSTMANAGELOGTABLENAME = "ForrasFSTManageLog";
        private readonly string FORRASCONNECTIONS_SPECHISTORYTABLENAME = "ForrasConnections_SpecHistory";
        private readonly string FORRASMODULEEXECUTIONS_SPECHISTORYTABLENAME = "ForrasModuleExecutions_SpecHistory";
        private readonly string FUVLOGTABLENAME = "fuv_Log";

        public GLogLoggedDataRemoverService( ILogger<GLogCleanerService> logger, IDbContextFactory<InstallDbContext> installContextFactory,
            IFUVGLogHelper fuvHelper )
        {
            _logger = logger;
            _installContextFactory = installContextFactory;
            _fuvHelper = fuvHelper;
        }

        public async Task RemoveLoggedDataFromList( List<RemoveLoggedDataParam> loggedDataList)
        {
            var brokerDataIdList = loggedDataList.Where(w => w.brokerDataSource != BrokerDataSource.FUVLog).Select(s => s.glogBrokerDataId).ToHashSet();

            await using var context = await _installContextFactory.CreateDbContextAsync();
            var brokerData = context.GLogBrokerData.AsTracking().Where(w => brokerDataIdList.Contains(w.Id));
            context.GLogBrokerData.RemoveRange(brokerData);
            await context.SaveChangesAsync();

            _logger.LogDebugWithLevelCheck($"{nameof(RemoveLoggedDataFromList)} starting with deleteOriginalLog = {loggedDataList.Select(s => s.deleteOriginal).ToArray()}.");

            foreach (RemoveLoggedDataParam loggedData in loggedDataList)
            {
                if (loggedData.deleteOriginal)
                {
                    await DeleteOriginalAsync(loggedData.brokerDataSource, loggedData.externalId);
                }
            }
            _logger.LogDebugWithLevelCheck( $"{nameof( RemoveLoggedDataFromList )} ended." );
        }

        /// <summary>
        /// Delete original log data from the install or fuv database.
        /// </summary>
        /// <param name="brokerDataSource">Original log data source.</param>
        /// <param name="externalId">Id of the original log data.</param>
        /// <returns></returns>
        private async Task DeleteOriginalAsync( BrokerDataSource brokerDataSource, string? externalId )
        {
            string sql = "DELETE FROM ";
            string? id = externalId;
            bool fuv = false;
            switch( brokerDataSource )
            {
                case BrokerDataSource.ForrasEventLog:
                    sql += FORRASEVENTLOGTABLENAME;
                    break;
                case BrokerDataSource.ForrasAppCallsLog:
                    sql += FORRASAPPCALLSLOGTABLENAME;
                    id = id?.Split( "(" )[0];
                    break;
                case BrokerDataSource.ForrasFSTManageLog:
                    sql += FORRASFSTMANAGELOGTABLENAME;
                    break;
                case BrokerDataSource.ForrasConnections_SpecHistory:
                    sql += FORRASCONNECTIONS_SPECHISTORYTABLENAME;
                    break;
                case BrokerDataSource.ForrasModuleExecutions_SpecHistory:
                    sql += FORRASMODULEEXECUTIONS_SPECHISTORYTABLENAME;
                    break;
                case BrokerDataSource.FUVLog:
                    sql += FUVLOGTABLENAME;
                    fuv = true;
                    break;
                case BrokerDataSource.Indefinite:
                default:
                    _logger.LogDebugWithLevelCheck($"{nameof(DeleteOriginalAsync)} unknown {nameof(brokerDataSource)} ({brokerDataSource}).");
                    return;
            }

            DbContext context;
            if( fuv )
            {
                context = await _fuvHelper.GetFuvDbContextAsync();
                sql += $" WHERE Id = {id}";
            }
            else
            {
                context = await _installContextFactory.CreateDbContextAsync();
                sql += $" WHERE ForrasID = {id}";
            }
            int numOfRowsAffected = await context.Database.ExecuteSqlRawAsync( sql );

            if(numOfRowsAffected == 0 )
            {
                _logger.LogDebugWithLevelCheck( $"{nameof( DeleteOriginalAsync )} could not find original log data to delete in {brokerDataSource} with id = {id}" );
            }
            else
            {
                _logger.LogDebugWithLevelCheck( $"{nameof(DeleteOriginalAsync)} deleted original log data from {brokerDataSource} with id = {id}" );
            }
        }
    }

    public class RemoveLoggedDataParam
    {
        public int glogBrokerDataId;
        public string? externalId;
        public BrokerDataSource brokerDataSource;
        public bool deleteOriginal;

        public RemoveLoggedDataParam(int glogBrokerDataId, string? externalId, BrokerDataSource brokerDataSource, bool deleteOriginal)
        {
            this.glogBrokerDataId = glogBrokerDataId;
            this.externalId = externalId;
            this.brokerDataSource = brokerDataSource;
            this.deleteOriginal = deleteOriginal;
        }
    }
}
