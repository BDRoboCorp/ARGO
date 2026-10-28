using BD.Model;
using System;
using System.Linq;
using System.Web;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Generates the configuration item URL for a request intake.
    /// </summary>
    public class GenerateConfigurationItemUrl : BusinessBase<bdgas_requestintake>
    {
        public GenerateConfigurationItemUrl(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.bdgas_RequestIntakeType != bdgas_requestintake_bdgas_requestintaketype.RPA) return;

            var name = Target.bdgas_digitalproductname;
            if (string.IsNullOrEmpty(name)) return;

            var environmentUrl = AdminService.GetEnvironmentVariable("bdgas_ARGOConfigurationItemURL");
            if (string.IsNullOrEmpty(environmentUrl)) return;

            var urls = environmentUrl.Split(new[] { "?" }, StringSplitOptions.RemoveEmptyEntries);
            if (urls.Length == 1) return;

            var baseUrl = urls.FirstOrDefault();
            var botName = $"BOT - {name}";
            var queryParams = HttpUtility.UrlEncode(urls.LastOrDefault().Replace("{name}", botName));

            Target.bdgas_configurationitemlink = $"{baseUrl}?{queryParams}";
        }
    }
}
