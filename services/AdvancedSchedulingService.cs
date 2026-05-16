using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using PinayPalBackupManager.Models;

namespace PinayPalBackupManager.Services
{
    /// <summary>
    /// Advanced scheduling service with cron-like expressions and dependency management
    /// </summary>
    public class AdvancedSchedulingService : IDisposable
    {
        private readonly System.Timers.Timer _schedulerTimer;
        private readonly Dictionary<string, ScheduledTask> _scheduledTasks;
        private readonly Dictionary<string, List<string>> _taskDependencies;
        private readonly Dictionary<string, TaskExecutionStatus> _taskStatuses;
        private readonly object _lock = new object();
        private bool _isRunning;

        public event Action<string, TaskExecutionResult>? OnTaskCompleted;
        public event Action<string, string>? OnTaskFailed;
        public event Action<string, int>? OnTaskProgress;

        public AdvancedSchedulingService()
        {
            _scheduledTasks = new Dictionary<string, ScheduledTask>();
            _taskDependencies = new Dictionary<string, List<string>>();
            _taskStatuses = new Dictionary<string, TaskExecutionStatus>();
            _schedulerTimer = new System.Timers.Timer(1000); // Check every second
            _schedulerTimer.Elapsed += CheckScheduledTasks;
        }

        /// <summary>
        /// Starts the scheduling service
        /// </summary>
        public void Start()
        {
            if (!_isRunning)
            {
                _isRunning = true;
                _schedulerTimer.Start();
                LogService.WriteSystemLog("[ADVANCED_SCHEDULER] Service started", "Information", "SYSTEM");
            }
        }

        /// <summary>
        /// Stops the scheduling service
        /// </summary>
        public void Stop()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _schedulerTimer.Stop();
                LogService.WriteSystemLog("[ADVANCED_SCHEDULER] Service stopped", "Information", "SYSTEM");
            }
        }

        /// <summary>
        /// Adds a scheduled task with cron expression
        /// </summary>
        public void AddScheduledTask(string taskId, string cronExpression, Func<Task> taskAction, 
            List<string>? dependencies = null, int maxRetries = 3, TimeSpan? timeout = null)
        {
            lock (_lock)
            {
                var scheduledTask = new ScheduledTask
                {
                    Id = taskId,
                    CronExpression = cronExpression,
                    TaskAction = taskAction,
                    Dependencies = dependencies ?? new List<string>(),
                    MaxRetries = maxRetries,
                    Timeout = timeout ?? TimeSpan.FromMinutes(30),
                    CreatedAt = DateTime.UtcNow,
                    LastRun = null,
                    NextRun = CalculateNextRun(cronExpression),
                    IsEnabled = true
                };

                _scheduledTasks[taskId] = scheduledTask;
                
                if (dependencies != null && dependencies.Any())
                {
                    _taskDependencies[taskId] = dependencies;
                }

                _taskStatuses[taskId] = new TaskExecutionStatus
                {
                    TaskId = taskId,
                    Status = TaskStatus.Pending,
                    RetryCount = 0,
                    CreatedAt = DateTime.UtcNow
                };

                LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Task added: {taskId} (Cron: {cronExpression})", 
                    "Information", "SYSTEM");
            }
        }

        /// <summary>
        /// Removes a scheduled task
        /// </summary>
        public void RemoveScheduledTask(string taskId)
        {
            lock (_lock)
            {
                if (_scheduledTasks.Remove(taskId))
                {
                    _taskDependencies.Remove(taskId);
                    _taskStatuses.Remove(taskId);
                    LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Task removed: {taskId}", "Information", "SYSTEM");
                }
            }
        }

        /// <summary>
        /// Enables or disables a task
        /// </summary>
        public void SetTaskEnabled(string taskId, bool enabled)
        {
            lock (_lock)
            {
                if (_scheduledTasks.TryGetValue(taskId, out var task))
                {
                    task.IsEnabled = enabled;
                    if (enabled && task.NextRun == null)
                    {
                        task.NextRun = CalculateNextRun(task.CronExpression);
                    }
                    LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Task {taskId} {(enabled ? "enabled" : "disabled")}", 
                        "Information", "SYSTEM");
                }
            }
        }

        /// <summary>
        /// Gets all scheduled tasks
        /// </summary>
        public List<ScheduledTask> GetAllTasks()
        {
            lock (_lock)
            {
                return _scheduledTasks.Values.ToList();
            }
        }

        /// <summary>
        /// Gets task execution status
        /// </summary>
        public TaskExecutionStatus? GetTaskStatus(string taskId)
        {
            lock (_lock)
            {
                return _taskStatuses.TryGetValue(taskId, out var status) ? status : null;
            }
        }

        /// <summary>
        /// Manually triggers a task execution
        /// </summary>
        public async Task<bool> TriggerTaskAsync(string taskId)
        {
            lock (_lock)
            {
                if (!_scheduledTasks.TryGetValue(taskId, out var task) || !task.IsEnabled)
                    return false;
            }

            await ExecuteTask(taskId);
            return true;
        }

        private void CheckScheduledTasks(object? sender, ElapsedEventArgs e)
        {
            if (!_isRunning) return;

            var now = DateTime.UtcNow;
            var tasksToRun = new List<string>();

            lock (_lock)
            {
                foreach (var kvp in _scheduledTasks)
                {
                    var task = kvp.Value;
                    if (task.IsEnabled && task.NextRun <= now && task.NextRun != null)
                    {
                        // Check dependencies
                        if (AreDependenciesSatisfied(task.Id))
                        {
                            tasksToRun.Add(task.Id);
                        }
                    }
                }
            }

            // Execute tasks outside the lock
            foreach (var taskId in tasksToRun)
            {
                _ = Task.Run(async () => await ExecuteTask(taskId));
            }
        }

        private async Task ExecuteTask(string taskId)
        {
            ScheduledTask? task;
            TaskExecutionStatus status;

            lock (_lock)
            {
                if (!_scheduledTasks.TryGetValue(taskId, out task) || !task.IsEnabled)
                    return;

                status = _taskStatuses[taskId];
                status.Status = TaskStatus.Running;
                status.StartedAt = DateTime.UtcNow;
            }

            try
            {
                LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Executing task: {taskId}", "Information", "SYSTEM");

                // Report progress
                OnTaskProgress?.Invoke(taskId, 0);

                // Execute with timeout
                using var cts = new CancellationTokenSource(task.Timeout);
                var taskCompletion = task.TaskAction();
                var timeoutTask = Task.Delay(task.Timeout, cts.Token);

                var completedTask = await Task.WhenAny(taskCompletion, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Task {taskId} timed out after {task.Timeout}");
                }

                await taskCompletion;

                // Update next run time
                lock (_lock)
                {
                    task.LastRun = DateTime.UtcNow;
                    task.NextRun = CalculateNextRun(task.CronExpression);
                    status.Status = TaskStatus.Completed;
                    status.CompletedAt = DateTime.UtcNow;
                    status.RetryCount = 0;
                }

                var result = new TaskExecutionResult
                {
                    TaskId = taskId,
                    Success = true,
                    ExecutedAt = DateTime.UtcNow,
                    Duration = (status.StartedAt.HasValue ? DateTime.UtcNow - status.StartedAt.Value : TimeSpan.Zero),
                    Message = "Task completed successfully"
                };

                OnTaskCompleted?.Invoke(taskId, result);
                LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Task completed: {taskId}", "Information", "SYSTEM");
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    status.Status = TaskStatus.Failed;
                    status.CompletedAt = DateTime.UtcNow;
                    status.LastError = ex.Message;
                    status.RetryCount++;
                }

                OnTaskFailed?.Invoke(taskId, ex.Message);
                LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Task failed: {taskId} - {ex.Message}", "Error", "SYSTEM");

                // Retry logic
                if (status.RetryCount < task.MaxRetries)
                {
                    var retryDelay = CalculateRetryDelay(status.RetryCount);
                    LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Retrying task {taskId} in {retryDelay.TotalSeconds}s (Attempt {status.RetryCount + 1}/{task.MaxRetries})", 
                        "Information", "SYSTEM");

                    await Task.Delay(retryDelay);
                    _ = Task.Run(async () => await ExecuteTask(taskId));
                }
                else
                {
                    LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Task {taskId} failed after {task.MaxRetries} retries", "Error", "SYSTEM");
                }
            }
        }

        private bool AreDependenciesSatisfied(string taskId)
        {
            if (!_taskDependencies.TryGetValue(taskId, out var dependencies))
                return true;

            foreach (var depId in dependencies)
            {
                if (_taskStatuses.TryGetValue(depId, out var depStatus))
                {
                    // Dependency must have completed successfully in the last hour
                    if (depStatus.Status != TaskStatus.Completed || 
                        depStatus.CompletedAt == null ||
                        DateTime.UtcNow - depStatus.CompletedAt.Value > TimeSpan.FromHours(1))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private DateTime CalculateNextRun(string cronExpression)
        {
            try
            {
                var now = DateTime.UtcNow;
                var next = ParseCronExpression(cronExpression, now);
                return next;
            }
            catch (Exception ex)
            {
                LogService.WriteSystemLog($"[ADVANCED_SCHEDULER] Invalid cron expression '{cronExpression}': {ex.Message}", "Error", "SYSTEM");
                return DateTime.UtcNow.AddHours(1); // Default to 1 hour from now
            }
        }

        private DateTime ParseCronExpression(string cronExpression, DateTime fromTime)
        {
            // Simple cron parser - supports basic formats like "0 */6 * * *" (every 6 hours)
            // For production, consider using a proper cron library
            
            var parts = cronExpression.Split(' ');
            if (parts.Length != 5)
                throw new ArgumentException("Invalid cron expression format");

            var minute = parts[0];
            var hour = parts[1];
            var day = parts[2];
            var month = parts[3];
            var dayOfWeek = parts[4];

            var nextTime = new DateTime(fromTime.Year, fromTime.Month, fromTime.Day, fromTime.Hour, fromTime.Minute, 0, DateTimeKind.Utc);
            nextTime = nextTime.AddMinutes(1); // Start from next minute

            // Simple implementation - check every minute for next match
            for (int i = 0; i < 365 * 24 * 60; i++) // Check up to 1 year ahead
            {
                if (MatchesCronField(nextTime.Minute, minute) &&
                    MatchesCronField(nextTime.Hour, hour) &&
                    MatchesCronField(nextTime.Day, day) &&
                    MatchesCronField(nextTime.Month, month) &&
                    MatchesCronField((int)nextTime.DayOfWeek, dayOfWeek))
                {
                    return nextTime;
                }
                nextTime = nextTime.AddMinutes(1);
            }

            throw new ArgumentException("Could not find next run time within 1 year");
        }

        private bool MatchesCronField(int value, string cronField)
        {
            if (cronField == "*") return true;

            // Handle specific values and ranges (basic implementation)
            if (int.TryParse(cronField, out var fieldValue))
                return value == fieldValue;

            // Handle comma-separated values
            if (cronField.Contains(','))
            {
                var values = cronField.Split(',');
                return values.Any(v => int.TryParse(v.Trim(), out var val) && val == value);
            }

            // Handle step values like */6
            if (cronField.StartsWith("*/"))
            {
                if (int.TryParse(cronField.Substring(2), out var step))
                    return value % step == 0;
            }

            return false;
        }

        private TimeSpan CalculateRetryDelay(int retryCount)
        {
            // Exponential backoff: 30s, 2min, 8min, 32min, etc.
            return TimeSpan.FromSeconds(30 * Math.Pow(4, retryCount - 1));
        }

        public void Dispose()
        {
            Stop();
            _schedulerTimer?.Dispose();
        }
    }

    public class ScheduledTask
    {
        public string Id { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public Func<Task> TaskAction { get; set; } = () => Task.CompletedTask;
        public List<string> Dependencies { get; set; } = new();
        public int MaxRetries { get; set; } = 3;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class TaskExecutionStatus
    {
        public string TaskId { get; set; } = string.Empty;
        public TaskStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int RetryCount { get; set; }
        public string? LastError { get; set; }
    }

    public class TaskExecutionResult
    {
        public string TaskId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime ExecutedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public enum TaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }
}
