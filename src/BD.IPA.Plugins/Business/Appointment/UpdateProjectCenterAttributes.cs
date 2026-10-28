using BD.Model;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Updates project center attributes on an appointment from related records.
    /// </summary>
    public class UpdateProjectCenterAttributes : BusinessBase<Model.Appointment>
    {
        public UpdateProjectCenterAttributes(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            var projectCenterRef = Latest.RegardingObjectId;
            if (projectCenterRef == null) return;
            if (projectCenterRef.LogicalName != bdgas_requestintake.EntityLogicalName) return;

            var update = new bdgas_requestintake { Id = projectCenterRef.Id };
            if (Target.Contains(Model.Appointment.Fields.bdgas_gxpapplicableid))
            {
                update.bdgas_gxpapplicableid = Latest.bdgas_gxpapplicableid;
            }

            if (Target.Contains(Model.Appointment.Fields.bdgas_gxpapplicabilityid))
            {
                update.bdgas_systemcategoryid = Latest.bdgas_gxpapplicabilityid;
            }

            if (Target.Contains(Model.Appointment.Fields.bdgas_soxapplicableid))
            {
                update.bdgas_soxapplicableid = Latest.bdgas_soxapplicableid;
            }

            if (Target.Contains(Model.Appointment.Fields.bdgas_piistoredormanagedintheappid))
            {
                update.bdgas_privacyapplicabilityid = Latest.bdgas_piistoredormanagedintheappid;
            }

            if (Target.Contains(Model.Appointment.Fields.bdgas_drplanrequiredfromcmdbbusinessappid))
            {
                update.bdgas_drplanstatusid = Latest.bdgas_drplanrequiredfromcmdbbusinessappid;
            }

            if (Target.Contains(Model.Appointment.Fields.bdgas_otherregulationsorlegalconstraintimpactid))
            {
                update.bdgas_bdgas_otherregulationsorlegalconstraintim = Latest.bdgas_otherregulationsorlegalconstraintimpactid;
            }

            if (update.Attributes.Count > 1)
            {
                AdminService.Update(update);
            }
        }
    }
}
