using System;
using System.Collections;
using UnityEngine;

namespace mod_update_manager
{
    /// <summary>
    /// Manages scheduled background update checking
    /// </summary>
    public class UpdateScheduler
    {
        private float _checkIntervalSeconds;
        private float _timeSinceLastCheck;
        private bool _isEnabled;
        private UpdateChecker _updateChecker;
        private MonoBehaviour _coroutineRunner;
        private bool _isChecking;

        public event Action<string> OnScheduledCheckStarted;
        public event Action<string> OnScheduledCheckCompleted;

        public bool IsRunning => _isEnabled;

        public UpdateScheduler(UpdateChecker updateChecker, MonoBehaviour coroutineRunner)
        {
            _updateChecker = updateChecker;
            _coroutineRunner = coroutineRunner;
            _isEnabled = false;
            _timeSinceLastCheck = 0;
            _isChecking = false;
        }

        /// <summary>
        /// Start scheduled checking
        /// </summary>
        public void Start(int intervalMinutes)
        {
            if (intervalMinutes < 10 || intervalMinutes > 1440)
            {
                Plugin.Logger.LogWarning("Interval must be between 10 and 1440 minutes");
                return;
            }

            _checkIntervalSeconds = intervalMinutes * 60f;
            _isEnabled = true;
            _timeSinceLastCheck = 0;
            Plugin.Logger.LogInfo($"Scheduled update checking started (interval: {intervalMinutes} minutes)");
        }

        /// <summary>
        /// Stop scheduled checking
        /// </summary>
        public void Stop()
        {
            _isEnabled = false;
            Plugin.Logger.LogInfo("Scheduled update checking stopped");
        }

        /// <summary>
        /// Update method - should be called from Plugin Update()
        /// </summary>
        public void Update()
        {
            if (!_isEnabled || _isChecking)
                return;

            _timeSinceLastCheck += Time.deltaTime;

            if (_timeSinceLastCheck >= _checkIntervalSeconds)
            {
                _timeSinceLastCheck = 0;
                ExecuteScheduledCheck();
            }
        }

        /// <summary>
        /// Reset the timer (useful after manual checks)
        /// </summary>
        public void ResetTimer()
        {
            _timeSinceLastCheck = 0;
        }

        /// <summary>
        /// Get time until next scheduled check in seconds
        /// </summary>
        public float GetTimeUntilNextCheck()
        {
            return Mathf.Max(0, _checkIntervalSeconds - _timeSinceLastCheck);
        }

        /// <summary>
        /// Get time until next scheduled check as formatted string
        /// </summary>
        public string GetTimeUntilNextCheckFormatted()
        {
            var seconds = GetTimeUntilNextCheck();
            var mins = (int)(seconds / 60);
            var secs = (int)(seconds % 60);

            if (mins > 60)
            {
                var hours = mins / 60;
                var remainingMins = mins % 60;
                return $"{hours}h {remainingMins}m";
            }

            return $"{mins}m {secs}s";
        }

        private void ExecuteScheduledCheck()
        {
            if (!_updateChecker.IsChecking)
            {
                _isChecking = true;
                OnScheduledCheckStarted?.Invoke("Scheduled update check starting...");
                Plugin.Logger.LogInfo("Executing scheduled update check");

                // Subscribe to completion event
                _updateChecker.OnAllChecksComplete += OnCheckCompleted;
                _updateChecker.CheckAllMods(_coroutineRunner);
            }
        }

        private void OnCheckCompleted(System.Collections.Generic.List<InstalledModInfo> mods)
        {
            _isChecking = false;
            _updateChecker.OnAllChecksComplete -= OnCheckCompleted;

            var updatesFound = mods.FindAll(m => m.NeedsUpdate).Count;
            var message = $"Scheduled check completed. {updatesFound} update(s) available.";

            OnScheduledCheckCompleted?.Invoke(message);
            Plugin.Logger.LogInfo(message);
        }
    }
}
