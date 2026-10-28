using BD.Model;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace BD.IPA.Plugins.Business.Appointment
{
    /// <summary>
    /// Assigns the configured autonumber to an appointment.
    /// </summary>
    public class SetAutonumber : BusinessBase<Model.Appointment>
    {
        public SetAutonumber(ILocalPluginContext context, StateManager<Model.Appointment> stateManager) : base(context, stateManager)
        {
        }

        public override void HandleExecute()
        {
            if (Latest.bdgas_SlotStatus == null) return;

            var autonumber = GetAutonumber("SRA");

            // Lock for concurrency purposes
            var updateAutonumber = new bdgas_autonumber { Id = autonumber.Id };
            AdminService.Update(updateAutonumber);

            var seq = autonumber.bdgas_sequence + 1;

            Target.bdgas_requestname = $"SRA{seq:D7}";

            // Release the lock
            var seqUpdate = new bdgas_autonumber { Id = autonumber.Id, bdgas_sequence = seq };
            AdminService.Update(seqUpdate);
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
