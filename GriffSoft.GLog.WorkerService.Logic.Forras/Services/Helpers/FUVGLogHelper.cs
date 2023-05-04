using GriffSoft.Forras.DataAccess.FUV;
using GriffSoft.Forras.DataAccess.FUV.Entities;
using GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts;
using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.GLog.WorkerService.Shared.Extensions;
using GriffSoft.Forras.DataAccess.FUV.Contracts;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Helpers
{
    public class FUVLogMemory : IFUVLogMemory<FUVLogMemory>
    {
        private int _rangeFrom;
        private int _rangeTo;
        private string? _cachedConnectionString;

        public FUVLogMemory()
        {
            _rangeTo = 0;
            _rangeFrom = 0;
            _cachedConnectionString = null;
        }

        public string? GetConnectionString()
        {
            if( string.IsNullOrWhiteSpace( _cachedConnectionString ) )
            {
                return null;
            }
            else
            {
                return _cachedConnectionString;
            }
        }
        public void SetConnectionString( string connectionString )
        {
            _cachedConnectionString = connectionString;
        }

        public int GetRangeFrom()
        {
            return _rangeFrom;
        }
        public int GetRangeTo()
        {
            return _rangeTo;
        }

        public void UpdateRange( int from, int to )
        {
            _rangeFrom = from;
            _rangeTo = to;
        }
    }
    public class FUVGLogHelper : IFUVGLogHelper
    {
        private readonly ILogger<FUVGLogHelper> _logger;
        private readonly IFuvDbContextHandler _fuvDbContextHelper;
        private readonly IFUVLogMemory<FUVLogMemory> _memory;

        public FUVGLogHelper( ILogger<FUVGLogHelper> logger, IFuvDbContextHandler fuvDbContextFactory,
            IFUVLogMemory<FUVLogMemory> memory)
        {
            _logger = logger;
            _fuvDbContextHelper = fuvDbContextFactory;
            _memory = memory;
        }

        
        public async Task<FUVLogCheckoutData?> CheckOutFuvLogData( int maxLogDataCapturedInCycles, string uniqueId )
        {
            using( FUVDbContext fuvContext = await this.GetFuvDbContextAsync() )
            {
                FUV_GLog? checkoutDataOfThisService = await fuvContext.FUV_GLog.Where( glog => glog.BackGroundServiceUniqueId == uniqueId ).FirstOrDefaultAsync();

                if( checkoutDataOfThisService is not null )
                {
                    switch( checkoutDataOfThisService.Status )
                    {
                        case (int)FUVGLogDataStatus.InWriterQueue:
                            return null;
                        case (int)FUVGLogDataStatus.Processed:
                            return await CreateFuvCheckoutData( fuvContext, maxLogDataCapturedInCycles, checkoutDataOfThisService, uniqueId );
                        case (int)FUVGLogDataStatus.UnderProcess:
                            return await RecreateFuvCheckoutData( fuvContext, checkoutDataOfThisService );
                        default:
                            throw new ArgumentException("Hibás CheckoutData");
                    }
                }
                else
                {
                    checkoutDataOfThisService = new FUV_GLog();
                    await fuvContext.FUV_GLog.AddAsync( checkoutDataOfThisService );
                    return await CreateFuvCheckoutData( fuvContext, maxLogDataCapturedInCycles, checkoutDataOfThisService, uniqueId );
                } 
            }
        }

        private async Task<FUVLogCheckoutData> RecreateFuvCheckoutData( FUVDbContext fuvContext, FUV_GLog checkoutDataOfThisService )
        {
            _memory.UpdateRange( checkoutDataOfThisService.RangeFrom, checkoutDataOfThisService.RangeTo );
            var retryData = new FUVLogCheckoutData()
            {
                FromId = checkoutDataOfThisService.RangeFrom,
                ToId = checkoutDataOfThisService.RangeTo,
                UniqueId = checkoutDataOfThisService.BackGroundServiceUniqueId,
                AllIds = await fuvContext.FUV_Log.AsNoTracking().Select( log => log.Id ).
                    Where( id => id >= checkoutDataOfThisService.RangeFrom && id <= checkoutDataOfThisService.RangeTo ).ToListAsync(),
            };
            return retryData;
        }

        private async Task<FUVLogCheckoutData?> CreateFuvCheckoutData( FUVDbContext fuvContext, int maxLogDataCapturedInCycles, FUV_GLog checkoutDataOfThisService, string uniqueId )
        {
            int lastCheckedOutId = await fuvContext.FUV_GLog.Select( glog => glog.RangeTo ).OrderByDescending( i => i ).FirstOrDefaultAsync();
            List<int> logIdsToCheckout = await fuvContext.FUV_Log.Select( log => log.Id ).Where( id => ( lastCheckedOutId > 0 ) ? ( id > lastCheckedOutId ) : true )
                            .OrderBy( id => id ).Take( maxLogDataCapturedInCycles ).ToListAsync();

            if( !logIdsToCheckout.Any() )
            {
                _logger.LogDebugWithLevelCheck( $"{nameof( CheckOutFuvLogData )} logIdsToCheckout is null or empty" );
                return null;
            }

            int from = logIdsToCheckout.Min();
            int to = logIdsToCheckout.Max();

            checkoutDataOfThisService.RangeFrom = from;
            checkoutDataOfThisService.RangeTo = to;
            checkoutDataOfThisService.Status = (int)FUVGLogDataStatus.UnderProcess;
            checkoutDataOfThisService.BackGroundServiceUniqueId = uniqueId;

            await fuvContext.SaveChangesAsync();

            _logger.LogDebugWithLevelCheck( $"{nameof( CheckOutFuvLogData )} checked out fuvlog from id {from} to {to}" );

            _memory.UpdateRange( from, to );
            return new FUVLogCheckoutData()
            {
                FromId = from,
                ToId = to,
                UniqueId = uniqueId,
                AllIds = logIdsToCheckout.ToList(),
            };
        }

        public async Task ResetLogsStuckInWriterQueueAsnyc( string uniqueId )
        {
            try
            {
                using( FUVDbContext fuvContext = await this.GetFuvDbContextAsync() )
                {
                    var logsStuckInWriterQueue = fuvContext.FUV_GLog.Where( glog => glog.Status == (int)FUVGLogDataStatus.InWriterQueue && glog.BackGroundServiceUniqueId == uniqueId );
                    await logsStuckInWriterQueue.ForEachAsync( glog => glog.Status = (int)FUVGLogDataStatus.UnderProcess );
                    await fuvContext.SaveChangesAsync();
                }
            }
            catch( Exception ex )
            {
                _logger.LogError( ex, "Unhandled exception" );
                throw;
            }
        }

        public async Task SetFuvLogCheckoutToProcessedAsync( string uniqueId )
        {
            using( FUVDbContext fuvContext = await this.GetFuvDbContextAsync() )
            {
                var glogData = await fuvContext.FUV_GLog.Where( glog => glog.BackGroundServiceUniqueId == uniqueId ).FirstOrDefaultAsync();
                if( glogData is null )
                {
                    return;
                }
                glogData.Status = (int)FUVGLogDataStatus.Processed;
                await fuvContext.SaveChangesAsync();
            }
        }

        public async Task SetLogStatusToInWriterQeueueAsync( string uniqueId )
        {
            using( FUVDbContext fuvContext = await this.GetFuvDbContextAsync() )
            {
                var glogData = await fuvContext.FUV_GLog.Where( glog => glog.BackGroundServiceUniqueId == uniqueId ).FirstOrDefaultAsync();
                if( glogData is null )
                {
                    return;
                }
                glogData.Status = (int)FUVGLogDataStatus.InWriterQueue;
                await fuvContext.SaveChangesAsync();
            }
            
        }

        public async Task<FUVDbContext> GetFuvDbContextAsync()
        {
            string? cachedConnString = _memory.GetConnectionString();
            if( cachedConnString is null )
            {
                cachedConnString = await _fuvDbContextHelper.GetFuvDbConnectionStringAsync();
                _memory.SetConnectionString( cachedConnString );
            }
            return await _fuvDbContextHelper.CreateDbContextAsync( cachedConnString );
        }
    }
}
