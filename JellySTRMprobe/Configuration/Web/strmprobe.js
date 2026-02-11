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

    selectedLibraryIds: []
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
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initJellySTRMprobeConfig);
} else {
    initJellySTRMprobeConfig();
}
