var BDGAS = BDGAS || {};
BDGAS.SRAActivityCommands = BDGAS.SRAActivityCommands || {};

BDGAS.SRAActivityCommands.openSRAScheduling = function (firstSelectedItemId, selectedControl) {
    try {
        var recordId = null;

        // Option 1: Get selected record ID from command parameter
        if (firstSelectedItemId) {
            recordId = firstSelectedItemId.replace(/[{}]/g, "");
        }

        // Option 2: Fallback to selected grid row
        if (!recordId && selectedControl) {
            var grid = selectedControl.getGrid();
            var selectedRows = grid.getSelectedRows();

            if (selectedRows.getLength() > 0) {
                var selectedRow = selectedRows.get(0);
                recordId = selectedRow.getData().getEntity().getId().replace(/[{}]/g, "");
            }
        }

        if (!recordId) {
            Xrm.Navigation.openAlertDialog({
                text: "Please select one SRA Activity record first."
            });
            return;
        }

        var pageInput = {
            pageType: "custom",
            name: "bdgas_sraschedulingpage_2ec88",
            entityName: "bdgas_sraactivity",
            recordId: recordId
        };

        var navigationOptions = {
            target: 2,
            position: 1,
            width: {
                value: 80,
                unit: "%"
            },
            height: {
                value: 80,
                unit: "%"
            },
            title: "SRA Scheduling"
        };

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function () {
                // Called when the dialog closes.
            }
        ).catch(
            function (error) {
                Xrm.Navigation.openAlertDialog({
                    text: "Unable to open SRA Scheduling. " + error.message
                });
            }
        );

    } catch (e) {
        Xrm.Navigation.openAlertDialog({
            text: "Error opening SRA Scheduling. " + e.message
        });
    }
};