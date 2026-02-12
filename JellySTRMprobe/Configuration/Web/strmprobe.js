var JellySTRMprobeConfig = {

    pluginUniqueId: 'b8f5e3a1-d4c7-4f2e-9a6b-1c8d3e5f7a9b',

    loadConfig: function () {
        var self = this;

        ApiClient.getPluginConfiguration(self.pluginUniqueId).then(function (config) {
            document.getElementById('chkEnableCatchUpMode').checked = config.EnableCatchUpMode;
            document.getElementById('txtProbeParallelism').value = config.ProbeParallelism;
            document.getElementById('txtProbeTimeoutSeconds').value = config.ProbeTimeoutSeconds;
            document.getElementById('txtProbeCooldownMs').value = config.ProbeCooldownMs;
            document.getElementById('chkDeleteFailedStrms').checked = config.DeleteFailedStrms;
            document.getElementById('txtDeleteFailureThreshold').value = config.DeleteFailureThreshold;

            self.selectedLibraryIds = config.SelectedLibraryIds || [];
            self.loadLibraries();
        });
    },

    saveConfig: function () {
        var self = this;

        ApiClient.getPluginConfiguration(self.pluginUniqueId).then(function (config) {
            config.EnableCatchUpMode = document.getElementById('chkEnableCatchUpMode').checked;
            config.ProbeParallelism = parseInt(document.getElementById('txtProbeParallelism').value, 10);
            config.ProbeTimeoutSeconds = parseInt(document.getElementById('txtProbeTimeoutSeconds').value, 10);
            config.ProbeCooldownMs = parseInt(document.getElementById('txtProbeCooldownMs').value, 10);
            config.DeleteFailedStrms = document.getElementById('chkDeleteFailedStrms').checked;
            config.DeleteFailureThreshold = parseInt(document.getElementById('txtDeleteFailureThreshold').value, 10);
            config.SelectedLibraryIds = self.getSelectedLibraryIds();

            ApiClient.updatePluginConfiguration(self.pluginUniqueId, config).then(function () {
                Dashboard.processPluginConfigurationUpdateResult();
            });
        });
    },

    loadLibraries: function () {
        var self = this;
        var container = document.getElementById('libraryList');

        ApiClient.getVirtualFolders().then(function (folders) {
            var html = '';
            folders.forEach(function (folder) {
                var folderId = folder.ItemId;
                var folderIdLower = folderId.toLowerCase();
            var isChecked = self.selectedLibraryIds.some(function (id) { return id.toLowerCase() === folderIdLower; }) ? 'checked' : '';
                html += '<div class="checkboxContainer">';
                html += '<label class="emby-checkbox-label">';
                html += '<input is="emby-checkbox" type="checkbox" class="libraryCheckbox" ';
                html += 'data-library-id="' + folderId + '" ' + isChecked + '/>';
                html += '<span>' + folder.Name + '</span>';
                html += '</label>';
                html += '</div>';
            });
            container.innerHTML = html;
        });
    },

    getSelectedLibraryIds: function () {
        var ids = [];
        var checkboxes = document.querySelectorAll('.libraryCheckbox:checked');
        checkboxes.forEach(function (checkbox) {
            ids.push(checkbox.getAttribute('data-library-id'));
        });
        return ids;
    },

    selectedLibraryIds: [],

    loadSchedule: function () {
        ApiClient.getScheduledTasks().then(function (tasks) {
            var task = tasks.find(function (t) { return t.Key === 'StrmProbeMediaInfo'; });
            if (!task) { return; }

            var section = document.getElementById('scheduleSection');
            var container = document.getElementById('scheduleStatus');
            section.style.display = '';

            var lines = [];

            // Triggers
            if (task.Triggers && task.Triggers.length > 0) {
                var triggerDescs = task.Triggers.map(function (tr) {
                    if (tr.Type === 'DailyTrigger') {
                        return 'Daily at ' + JellySTRMprobeConfig.ticksToTime(tr.TimeOfDayTicks);
                    } else if (tr.Type === 'IntervalTrigger') {
                        var hours = tr.IntervalTicks / 36000000000;
                        return 'Every ' + hours + ' hour' + (hours !== 1 ? 's' : '');
                    } else if (tr.Type === 'StartupTrigger') {
                        return 'On startup';
                    } else if (tr.Type === 'WeeklyTrigger') {
                        return tr.DayOfWeek + ' at ' + JellySTRMprobeConfig.ticksToTime(tr.TimeOfDayTicks);
                    }
                    return tr.Type;
                });
                lines.push('<b>Schedule:</b> ' + triggerDescs.join(', '));
            } else {
                lines.push('<b>Schedule:</b> No triggers configured');
            }

            // State
            lines.push('<b>Status:</b> ' + task.State);

            // Last run
            if (task.LastExecutionResult) {
                var r = task.LastExecutionResult;
                var endDate = new Date(r.EndTimeUtc);
                var timeStr = endDate.toLocaleString();
                var statusBadge = r.Status === 'Completed'
                    ? '<span style="color:#4caf50">' + r.Status + '</span>'
                    : '<span style="color:#f44336">' + r.Status + '</span>';
                lines.push('<b>Last run:</b> ' + timeStr + ' — ' + statusBadge);
            }

            container.innerHTML = lines.join('<br/>');
        });
    },

    ticksToTime: function (ticks) {
        var totalMinutes = Math.round(ticks / 600000000);
        var hours = Math.floor(totalMinutes / 60);
        var minutes = totalMinutes % 60;
        var ampm = hours >= 12 ? 'PM' : 'AM';
        var displayHours = hours % 12 || 12;
        return displayHours + ':' + (minutes < 10 ? '0' : '') + minutes + ' ' + ampm;
    }
};

function initJellySTRMprobeConfig() {
    var form = document.getElementById('JellySTRMprobeConfigForm');

    if (form) {
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            JellySTRMprobeConfig.saveConfig();
            return false;
        });
    }

    JellySTRMprobeConfig.loadConfig();
    JellySTRMprobeConfig.loadSchedule();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initJellySTRMprobeConfig);
} else {
    initJellySTRMprobeConfig();
}
