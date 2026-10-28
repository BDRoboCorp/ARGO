using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Removes BD-controlled segmentation data associated with an appointment.
    /// </summary>
    public class DeleteBDControlledSegmentation : BusinessBase<Model.Appointment>
    {
        public DeleteBDControlledSegmentation(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var systemResideText = Target.bdgas_systemresidetext?.ToLower();
            if (!systemResideText.Contains("bd controlled network")) return;

            var data = GetSelectedControlledSegmentationAssignment();
            foreach (var item in data)
            {
                AdminService.Delete(item.LogicalName, item.Id);
            }

            Target.bdgas_bdcontrolledsegmentationtext = "";
        }

        private bdgas_controlledsegmentationassignment[] GetSelectedControlledSegmentationAssignment()
        {
            var query = new QueryExpression(bdgas_controlledsegmentationassignment.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(false)
            };
            query.Criteria.AddCondition(bdgas_controlledsegmentationassignment.Fields.bdgas_appointmentid, ConditionOperator.Equal, Latest.Id);
            query.Criteria.AddCondition(bdgas_controlledsegmentationassignment.Fields.statecode, ConditionOperator.Equal, (int)bdgas_controlledsegmentationassignment_statecode.Active);

            var result = AdminService.RetrieveMultiple(query);

            return result.Entities.Select(e => e.ToEntity<bdgas_controlledsegmentationassignment>()).ToArray();
        }
    }
}
