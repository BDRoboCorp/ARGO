using BD.IPA.Plugins.Business;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before an SRA appointment assignment is deleted.
    /// </summary>
    public class PreSRAAppointmentAssignmentDelete : PluginBase
    {
        public PreSRAAppointmentAssignmentDelete() : base(typeof(PreSRAAppointmentAssignmentDelete))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_sraappointmentassignment>(localPluginContext);

            new Business.SRAAppointmentAssignment.UpsertEmail(localPluginContext, stateManager).Execute();
        }
    }
}
