using BD.IPA.Plugins.Business;
using BD.IPA.Plugins.Business.PublicHoliday;
using BD.Model;

namespace BD.IPA.Plugins
{
    /// <summary>
    /// Runs business processing before a public holiday is created.
    /// </summary>
    public class PrePublicHolidayCreate : PluginBase
    {
        public PrePublicHolidayCreate() : base(typeof(PrePublicHolidayCreate))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            var stateManager = new StateManager<bdgas_publicholiday>(localPluginContext);

            if (stateManager.Target.Contains(bdgas_publicholiday.Fields.bdgas_date))
            {
                new DeleteAppointment(localPluginContext, stateManager).Execute();
            }
        }
    }
}
