using GriffSoft.GLog.WorkerService.Logic.Forras.Types;
using GriffSoft.Forras.DataAccess.FUV.Entities;
using GriffSoft.Forras.DataAccess.FUV;

namespace GriffSoft.GLog.WorkerService.Logic.Forras.Services.Contracts
{
    public interface IFUVLogMemory<T> where T : class
    {
        public void UpdateRange(int from, int to);
        public int GetRangeFrom();
        public int GetRangeTo();
        public string? GetConnectionString();
        public void SetConnectionString(string connectionString);
    }
    public interface IFUVGLogHelper
    {
        /// <summary>
        /// Checkout next batch of fuv log data. If this service has checked out data with status = <see cref="FUVGLogDataStatus.InWriterQueue"/>
        /// then returns null, if status = <see cref="FUVGLogDataStatus.UnderProcess"/>, then it returns that.
        /// </summary>
        /// <param name="maxLogDataCapturedInCycles">Number of logs to store in fuvCheckoutBackgroundQueue.</param>
        /// <param name="uniqueId">The service uniqueId</param>
        /// <returns><see cref="FUVLogCheckoutData"/> which represents the data that has been checked out.</returns>
        public Task<FUVLogCheckoutData?> CheckOutFuvLogData( int maxLogDataCapturedInCycles, string uniqueId );

        /// <summary>
        /// If there is checked out data of the service with status = <see cref="FUVGLogDataStatus.InWriterQueue"/> then it resets to
        /// <see cref="FUVGLogDataStatus.UnderProcess"/>.
        /// </summary>
        /// <param name="uniqueId"></param>
        /// <returns>The service uniqueId</returns>
        public Task ResetLogsStuckInWriterQueueAsnyc( string uniqueId );

        /// <summary>
        /// Set the checked out data status of the service to <see cref="FUVGLogDataStatus.InWriterQueue"/>.
        /// </summary>
        /// <param name="uniqueId"></param>
        /// <returns>The service uniqueId</returns>
        public Task SetLogStatusToInWriterQeueueAsync( string uniqueId );

        /// <summary>
        /// Set the checked out data status of the service to <see cref="FUVGLogDataStatus.Processed"/>.
        /// </summary>
        /// <param name="uniqueId">The service uniqueId</param>
        /// <returns></returns>
        public Task SetFuvLogCheckoutToProcessedAsync( string uniqueId );

        public Task<FUVDbContext> GetFuvDbContextAsync();
    }
}
