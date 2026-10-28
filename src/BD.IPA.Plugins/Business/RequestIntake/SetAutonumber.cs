using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.RequestIntake
{
    /// <summary>
    /// Assigns the configured autonumber to a request intake.
    /// </summary>
    public class SetAutonumber : BusinessBase<bdgas_requestintake>
    {
        public SetAutonumber(ILocalPluginContext context, StateManager<bdgas_requestintake> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.bdgas_RequestIntakeType == null) return;

            var prefix = Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.QPA ?
                new Tuple<string, bdgas_requestintake_bdgas_requestintaketype>("QPA", bdgas_requestintake_bdgas_requestintaketype.QPA) : GetPrefix();

            var autonumberPrefix = Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.QPA ? "QPA" : Latest.bdgas_RequestIntakeType == bdgas_requestintake_bdgas_requestintaketype.RPA ? "RPA" : "DIR";
            var autonumber = GetAutonumber(autonumberPrefix);

            // Lock for concurrency purposes
            var updateAutonumber = new bdgas_autonumber { Id = autonumber.Id };
            AdminService.Update(updateAutonumber);

            var seq = autonumber.bdgas_sequence + 1;

            Target.bdgas_requestintake1 = $"{autonumberPrefix}{seq:D7}";
            Target.bdgas_RequestIntakeType = prefix.Item2;

            // Release the lock
            var seqUpdate = new bdgas_autonumber { Id = autonumber.Id, bdgas_sequence = seq };
            AdminService.Update(seqUpdate);
        }

        private Tuple<string, bdgas_requestintake_bdgas_requestintaketype> GetPrefix()
        {
            switch (Latest.bdgas_triagedtechnology.ToLower().Trim())
            {
                case "process digitalization": return new Tuple<string, bdgas_requestintake_bdgas_requestintaketype>("PEGA", bdgas_requestintake_bdgas_requestintaketype.PEGA);
                case "process mining": return new Tuple<string, bdgas_requestintake_bdgas_requestintaketype>("Process Mining", bdgas_requestintake_bdgas_requestintaketype.ProcessMining);
                case "data products, dashboards, & data intelligence": return new Tuple<string, bdgas_requestintake_bdgas_requestintaketype>("Data", bdgas_requestintake_bdgas_requestintaketype.Data);
                case "artificial intelligence & data science": return new Tuple<string, bdgas_requestintake_bdgas_requestintaketype>("AI", bdgas_requestintake_bdgas_requestintaketype.AI);
                default:
                    return new Tuple<string, bdgas_requestintake_bdgas_requestintaketype>("RPA", bdgas_requestintake_bdgas_requestintaketype.RPA);
            }
        }

        private bdgas_autonumber GetAutonumber(string value)
        {
            var name = $"{value}-Autonumber";

            var query = new QueryExpression(bdgas_autonumber.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(bdgas_autonumber.Fields.bdgas_sequence, bdgas_autonumber.Fields.bdgas_Type),
                TopCount = 1
            };
            query.Criteria.AddCondition(bdgas_autonumber.Fields.bdgas_Name, ConditionOperator.Equal, name);

            var result = AdminService.RetrieveMultiple(query);
            var current = result.Entities.FirstOrDefault()?.ToEntity<bdgas_autonumber>();

            if (current == null)
            {
                current = new bdgas_autonumber
                {
                    bdgas_sequence = 0,
                    bdgas_Name = $"{value}-Autonumber"
                };
                current.Id = AdminService.Create(current);
            }

            return current;
        }
    }
}
