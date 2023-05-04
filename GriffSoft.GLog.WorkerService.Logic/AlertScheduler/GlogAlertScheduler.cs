namespace GriffSoft.GLog.WorkerService.Logic.AlertScheduler
{
    public static class GLogTaskSchedulerExtensions
    {
        public static List<GLogTask> AddTaskToList( this List<GLogTask> list, TimeSpan span, Func<Task> taskFunc )
        {
            list.Add( new GLogTask( list, span, taskFunc ) );
            return list;
        }
        public static List<GLogTask> RemoveTaskFromList( this List<GLogTask> list, GLogTask task2Remove )
        {
            object locking = new();
            lock( locking )
            {
                list.Remove( task2Remove );
            }
            return list;
        }
    }

    public class GLogTaskScheduler
    {
        private List<GLogTask> _taskList;

        public GLogTaskScheduler()
        {
            _taskList = new List<GLogTask>();
        }

        public void AddTask( TimeSpan timeSpan, Func<Task> taskFunc )
        {
            _taskList.AddTaskToList( timeSpan, taskFunc );
        }
    }
}
