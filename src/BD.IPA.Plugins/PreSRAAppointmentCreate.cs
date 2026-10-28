using BD.IPA.Plugins.Business;
using BD.Model;
using System;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before an SRA appointment assignment is created.
    /// </summary>
    public class PreSRAAppointmentAssignmentCreate : PluginBase
    {
        public PreSRAAppointmentAssignmentCreate() : base(typeof(PreSRAAppointmentAssignmentCreate))
        {
        }

        public PreSRAAppointmentAssignmentCreate(Type pluginClassName) : base(pluginClassName)
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_sraappointmentassignment>(localPluginContext);

            new Business.SRAAppointmentAssignment.UpsertEmail(localPluginContext, stateManager).Execute();
        }
    }
}
